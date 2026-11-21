using System.Globalization;
using PharmacyStock;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Legacy.Tests;
public sealed class CharacterizationTests : IDisposable
{
    private readonly TextWriter previous = Console.Out;
    private readonly CultureInfo culture = CultureInfo.CurrentCulture;
    private readonly StringWriter output = new();
    private readonly string auditPath = AuditLog.FilePath;
    public CharacterizationTests()
    {
        Console.SetOut(output);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        AuditLog.Enabled = false;
    }
    public void Dispose()
    {
        Console.SetOut(previous);
        CultureInfo.CurrentCulture = culture;
        AuditLog.Enabled = true;
        AuditLog.FilePath = auditPath;
        output.Dispose();
    }
    private static (StockRepository Repo,PrescriptionService Service,Prescription Rx) Setup(
        int stock = 10, string category = "", int quantity = 2)
    {
        var repo = new StockRepository();
        repo.Medicines.Add(new Medicine("A", "Alpha", "same-inn", 100m));
        repo.AddBatch("A", "batch", DateTime.Today.AddDays(100), stock);
        var rx = new Prescription { Number="RX-X", MedicineCode="A",
            PatientName="Demo", Category=category, Quantity=quantity,
            IssuedOn=DateTime.Today, ValidDays=30, Phone="123456789" };
        repo.Prescriptions.Add(rx);
        return (repo,new PrescriptionService(repo),rx);
    }
    [Theory]
    [InlineData("", 200)]
    [InlineData("PENSIONER",100)]
    [InlineData("DISABLED",0)]
    [InlineData("UNKNOWN",200)]
    public void Dispense_Category_ReturnsCapturedAmount(string category,int expected)
    {
        var (_,service,rx)=Setup(category:category);
        var amount=service.Dispense(rx.Number,2,false,"PH");
        Assert.Equal(expected,amount);
        Assert.Equal(PrescriptionStatus.Dispensed,rx.Status);
        Assert.Equal(amount,rx.Paid);
    }
    [Theory]
    [InlineData(0)] [InlineData(-3)] [InlineData(99)]
    public void Dispense_RequestOutsideRemainder_IssuesAllRemaining(int requested)
    {
        var (_,service,rx)=Setup();
        Assert.Equal(200m,service.Dispense(rx.Number,requested,false,"PH"));
        Assert.Equal(2,rx.DispensedQty);
    }
    [Theory]
    [InlineData(0,0,PrescriptionStatus.Issued)]
    [InlineData(1,100,PrescriptionStatus.PartiallyDispensed)]
    public void Dispense_InsufficientStock_CapturesPartialOrZero(
        int stock,int expected,PrescriptionStatus state)
    {
        var (_,service,rx)=Setup(stock:stock);
        Assert.Equal(expected,service.Dispense(rx.Number,2,false,"PH"));
        Assert.Equal(state,rx.Status);
        Assert.Equal(stock,rx.DispensedQty);
    }
    [Theory]
    [InlineData(30,200,PrescriptionStatus.Dispensed)]
    [InlineData(31,-1,PrescriptionStatus.Expired)]
    public void Dispense_ValidityBoundary_CapturesInclusiveLastDay(
        int age,int expected,PrescriptionStatus state)
    {
        var (_,service,rx)=Setup();rx.IssuedOn=DateTime.Today.AddDays(-age);
        Assert.Equal(expected,service.Dispense(rx.Number,2,false,"PH"));
        Assert.Equal(state,rx.Status);
    }
    [Fact]
    public void Dispense_UnknownPrescription_ReturnsMinusOne()
    {
        var (_,service,_)=Setup();
        Assert.Equal(-1m,service.Dispense("missing",1,false,"PH"));
        Assert.Equal("Prescription missing not found",service.LastMessage);
    }
    [Fact]
    public void Dispense_ClosedPrescription_ReturnsMinusOne()
    {
        var (_,service,rx)=Setup();rx.Status=PrescriptionStatus.Cancelled;
        Assert.Equal(-1m,service.Dispense(rx.Number,1,false,"PH"));
        Assert.Equal(0,rx.DispensedQty);
    }
    [Fact]
    public void Dispense_ControlledWithoutPharmacist_Refuses()
    {
        var (repo,service,rx)=Setup();repo.Medicines[0].IsControlled=true;
        Assert.Equal(-1m,service.Dispense(rx.Number,2,true,""));
        Assert.Equal("Pharmacist code required for Alpha",service.LastMessage);
    }
    [Fact]
    public void Dispense_ControlledLimit_IssuesTwoAndLogs()
    {
        var (repo,service,rx)=Setup(quantity:4);
        repo.Medicines[0].IsControlled=true;repo.Medicines[0].MaxPerDispense=2;
        Assert.Equal(200m,service.Dispense(rx.Number,4,true,"PH"));
        Assert.Equal(PrescriptionStatus.PartiallyDispensed,rx.Status);
        Assert.Contains("[controlled log] Alpha x2, PH",output.ToString());
    }
    [Fact]
    public void Dispense_Generic_ChargesOriginalMedicinePrice()
    {
        // Characterization of existing pricing; TD-01, not a business requirement.
        var (repo,service,rx)=Setup(stock:0);
        repo.Medicines.Add(new Medicine("B","Beta","same-inn",20m));
        repo.AddBatch("B","b",DateTime.Today.AddDays(5),4);
        Assert.Equal(200m,service.Dispense(rx.Number,2,true,"PH"));
        Assert.Contains("Generic B",rx.History);
        Assert.Equal(2,repo.Batches[1].Qty);
    }
    [Fact]
    public void Dispense_ExpiredBatch_SkipsItAndUsesTodayBatch()
    {
        var (repo,service,rx)=Setup(stock:0);
        var expired=repo.AddBatch("A","old",DateTime.Today.AddDays(-1),9);
        var today=repo.AddBatch("A","today",DateTime.Today,2);
        Assert.Equal(200m,service.Dispense(rx.Number,2,false,"PH"));
        Assert.Equal(9,expired.Qty);Assert.Equal(0,today.Qty);
    }
    [Fact]
    public void Dispense_NegativeBatch_CapturesNegativePayment()
    {
        // TD-02: deliberately keep the surprising old result during refactoring.
        var (_,service,rx)=Setup(stock:-1);
        Assert.Equal(-100m,service.Dispense(rx.Number,2,false,"PH"));
        Assert.Equal(-1,rx.DispensedQty);
    }
    [Fact]
    public void Dispense_MissingMedicine_ThrowsNullReference()
    {
        var (repo,service,rx)=Setup();repo.Medicines.Clear();
        Assert.Throws<NullReferenceException>(()=>service.Dispense(rx.Number,2,false,"PH"));
    }
    [Theory]
    [InlineData("", "A", true)] [InlineData("Demo", "missing", true)]
    [InlineData("Demo", "A", false)]
    public void Issue_Input_CapturesNullOrNumber(string name,string code,bool rejected)
    {
        var (_,service,_)=Setup();
        var number=service.Issue(name,"","phone",code,1,0,"Doctor");
        Assert.Equal(rejected,number==null);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(11)]
    public void Issue_QuantityOutsideBounds_Throws(int quantity)
    {
        var (_,service,_)=Setup();
        Assert.Throws<ArgumentException>(()=>service.Issue("Demo","","","A",quantity,30,"D"));
    }
    [Theory]
    [InlineData(false,30)] [InlineData(true,10)]
    public void Issue_DefaultDays_CapturesThirtyOrTen(bool controlled,int expected)
    {
        var (repo,service,_)=Setup();repo.Medicines[0].IsControlled=controlled;
        var no=service.Issue("Demo","","","A",1,0,"D");
        Assert.Equal(expected,repo.FindPrescription(no).ValidDays);
    }
    [Theory]
    [InlineData(PrescriptionStatus.Issued,PrescriptionStatus.Cancelled,true)]
    [InlineData(PrescriptionStatus.Issued,PrescriptionStatus.Dispensed,false)]
    [InlineData(PrescriptionStatus.PartiallyDispensed,(PrescriptionStatus)99,true)]
    [InlineData(PrescriptionStatus.Dispensed,PrescriptionStatus.Cancelled,false)]
    public void ChangeStatus_Transition_CapturesOldDecision(
        PrescriptionStatus from,PrescriptionStatus to,bool expected)
    {
        var (_,service,rx)=Setup();rx.Status=from;
        Assert.Equal(expected,service.ChangeStatus(rx.Number,to));
        Assert.Equal(expected?to:from,rx.Status);
    }
    [Fact]
    public void ChangeStatus_Missing_ReturnsFalse()
    {
        var (_,service,_)=Setup();Assert.False(service.ChangeStatus("missing",PrescriptionStatus.Cancelled));
    }
    [Theory]
    [InlineData(89,false)] [InlineData(90,true)]
    public void Receive_ExpiryBoundary_CapturesNinetyDays(int days,bool expected)
    {
        var (repo,_,_)=Setup();var service=new InventoryService(repo);
        Assert.Equal(expected,service.Receive("A","new",DateTime.Today.AddDays(days),1,50));
    }
    [Theory]
    [InlineData(40,true)] [InlineData(41,false)]
    public void Receive_ShelfBoundary_CapturesFiftyPacks(int quantity,bool expected)
    {
        var (repo,_,_)=Setup();var service=new InventoryService(repo);
        Assert.Equal(expected,service.Receive("A","new",DateTime.Today.AddDays(100),quantity,99));
    }
    [Theory]
    [InlineData(null,"****")] [InlineData("12","****")] [InlineData("123456","***3456")]
    public void MaskedPhone_Input_CapturesFallback(string phone,string expected)
    {
        Assert.Equal(expected,new Prescription {Phone=phone}.MaskedPhone());
    }
    [Fact]
    public void PrintReceipt_ValidPrescription_PreservesText()
    {
        var (repo,_,rx)=Setup();rx.Paid=25;rx.DispensedQty=1;
        new ReportPrinter(repo).PrintReceipt(rx.Number);
        Assert.Contains("Receipt for Demo, phone ***6789",output.ToString());
        Assert.Contains("RX-X | Demo | A | 1/2 | Issued | paid 25.00",output.ToString());
    }
    [Fact]
    public void BatchesFor_ExpiredBatch_ReturnsItDespiteComment()
    {
        var (repo,_,_)=Setup();repo.AddBatch("A","old",DateTime.Today.AddDays(-1),1);
        Assert.Equal("old",repo.BatchesFor("A")[0].BatchNo);
    }
    [Fact]
    public void AuditLog_InvalidDirectory_SwallowsFailure()
    {
        AuditLog.Enabled=true;
        AuditLog.FilePath=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"a.txt");
        Assert.Null(Record.Exception(()=>AuditLog.Write("TEST","x")));
    }
}
