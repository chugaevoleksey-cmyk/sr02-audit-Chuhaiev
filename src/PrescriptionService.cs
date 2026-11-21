namespace PharmacyStock;

public class PrescriptionService
{
    private static readonly Dictionary<PrescriptionStatus, Func<PrescriptionStatus, bool>> StatusTransitions = new()
    {
        [PrescriptionStatus.Issued] = to => to == PrescriptionStatus.Cancelled || to == PrescriptionStatus.Expired,
        [PrescriptionStatus.PartiallyDispensed] = to => to > PrescriptionStatus.PartiallyDispensed,
    };
    private const int MaximumIssueQuantity = 10;
    private const int ControlledValidDays = 10;
    private const int DefaultValidDays = 30;
    public static int DispenseCount;   // для звіту зміни
    public StockRepository repo;
    public string LastMessage = "";    // причина відмови

    public PrescriptionService(StockRepository r)
    {
        repo = r;
    }

    // days: 0 = стандартні 30 днів (контрольовані - 5)
    // TODO TD-06: Issue залишає сім параметрів і рядкову категорію; до 12.12.2026.
    public string Issue(string patient, string category, string phone, string code, int qty,
        int days, string doctor)
    {
        LastMessage = "";
        if (string.IsNullOrEmpty(patient))
        {
            LastMessage = "Patient name is required";
            return null;
        }
        Medicine m = repo.FindMedicine(code);
        if (m == null)
        {
            LastMessage = "Unknown medicine " + code;
            return null;
        }
        if (qty <= 0 || qty > MaximumIssueQuantity)
            throw new ArgumentException("Quantity must be 1..10, got " + qty);
        if (m.IsControlled)
            days = ControlledValidDays;
        else if (days <= 0)
            days = DefaultValidDays;
        Prescription p = new Prescription();
        p.Number = repo.NextNumber();   // TODO: номер з eHealth
        p.PatientName = patient;
        p.Category = category;
        p.Phone = phone;
        p.MedicineCode = code;
        p.Quantity = qty;
        p.IssuedOn = DateTime.Now;
        p.ValidDays = days;
        p.Doctor = doctor;
        p.History.Add("Issued");
        repo.Prescriptions.Add(p);
        AuditLog.Write("ISSUE", p.Number + " " + code + " x" + qty);
        return p.Number;
    }

    public decimal Dispense(string prescriptionNumber, int requestedQuantity,
        bool allowGeneric, string pharmacistCode)
    {
        DispenseCount++;
        LastMessage = "";
        Prescription prescription = repo.FindPrescription(prescriptionNumber);
        if (!ValidatePrescription(prescription, prescriptionNumber))
            return -1;
        Medicine medicine = repo.FindMedicine(prescription.MedicineCode);
        int remainingQuantity = prescription.Quantity - prescription.DispensedQty;
        if (requestedQuantity <= 0 || requestedQuantity > remainingQuantity)
            requestedQuantity = remainingQuantity;
        if (medicine.IsControlled)
        {
            if (string.IsNullOrEmpty(pharmacistCode))
            {
                LastMessage = "Pharmacist code required for " + medicine.Name;
                return -1;
            }
            requestedQuantity = Math.Min(requestedQuantity, medicine.MaxPerDispense);
            allowGeneric = false;
            Console.WriteLine("  [controlled log] " + medicine.Name + " x" + requestedQuantity + ", " + pharmacistCode);
        }
        string medicineCode = SelectMedicineCode(prescription, medicine, requestedQuantity, allowGeneric);
        int dispensedQuantity = DeductBatches(medicineCode, requestedQuantity);
        if (dispensedQuantity == 0)
        {
            LastMessage = "Out of stock: " + medicine.Name;
            return 0;
        }
        UpdatePrescription(prescription, dispensedQuantity, pharmacistCode);
        // TODO TD-01: Дженерик оплачується за ціною початкового препарату; до 12.12.2026.
        decimal amount = BenefitPricing.CalculateCharge(prescription, medicine, dispensedQuantity);
        prescription.Paid = prescription.Paid + amount;
        AuditLog.Write("DISPENSE", prescriptionNumber + " " + medicineCode + " x" + dispensedQuantity + " = " + amount);
        return amount;
    }

    public bool ChangeStatus(string no, PrescriptionStatus to)
    {
        Prescription p = repo.FindPrescription(no);
        if (p == null)
            return false;
        bool ok = StatusTransitions.TryGetValue(p.Status, out var allowed) && allowed(to);
        if (!ok)
        {
            Console.WriteLine("Cannot change " + no + ": " + p.Status + " -> " + to);
            return false;
        }
        p.Status = to;
        p.History.Add(to.ToString());
        AuditLog.Write("STATUS", no + " -> " + to);
        return true;
    }



    private bool ValidatePrescription(Prescription prescription, string prescriptionNumber)
    {
        if (prescription == null)
        {
            LastMessage = "Prescription " + prescriptionNumber + " not found";
            return false;
        }
        if (prescription.Status > PrescriptionStatus.PartiallyDispensed)   // вже закритий
        {
            LastMessage = "Prescription " + prescriptionNumber + " is " + prescription.Status;
            return false;
        }
        int prescriptionAgeDays = (DateTime.Today - prescription.IssuedOn.Date).Days;
        if (prescriptionAgeDays > prescription.ValidDays)
        {
            prescription.Status = PrescriptionStatus.Expired;
            LastMessage = "Prescription " + prescriptionNumber + " has expired";
            return false;
        }
        return true;
    }

    private string SelectMedicineCode(Prescription prescription, Medicine medicine,
        int requestedQuantity, bool allowGeneric)
    {
        string medicineCode = medicine.Code;
        if (!allowGeneric || repo.UsableStock(medicineCode) >= requestedQuantity)
            return medicineCode;
        foreach (Medicine generic in repo.Medicines)
        {
            if (generic.Inn != medicine.Inn || generic.Code == medicineCode)
                continue;
            if (repo.UsableStock(generic.Code) < requestedQuantity)
                continue;
            prescription.History.Add("Generic " + generic.Code);
            return generic.Code;
        }
        return medicineCode;
    }

    private int DeductBatches(string medicineCode, int requestedQuantity)
    {
        int dispensedQuantity = 0;
        foreach (StockBatch batch in repo.BatchesFor(medicineCode))
        {
            if (batch.ExpiryDate < DateTime.Today)
                continue;
            // TODO TD-02: Від’ємний залишок партії дає від’ємну оплату; до 28.11.2026.
            int quantityFromBatch = Math.Min(batch.Qty, requestedQuantity - dispensedQuantity);
            batch.Qty = batch.Qty - quantityFromBatch;
            dispensedQuantity = dispensedQuantity + quantityFromBatch;
        }
        return dispensedQuantity;
    }

    private static void UpdatePrescription(Prescription prescription, int dispensedQuantity, string pharmacistCode)
    {
        prescription.DispensedQty = prescription.DispensedQty + dispensedQuantity;
        prescription.LastPharmacist = pharmacistCode;
        if (prescription.DispensedQty >= prescription.Quantity)
            prescription.Status = PrescriptionStatus.Dispensed;
        else
            prescription.Status = PrescriptionStatus.PartiallyDispensed;
        prescription.History.Add(prescription.Status + " " + dispensedQuantity);
    }








}
