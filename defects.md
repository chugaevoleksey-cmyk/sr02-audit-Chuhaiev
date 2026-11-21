# Реєстр дефектів вихідного коду

Місця стосуються тегу baseline, а не змінених файлів. Пріоритет = серйозність × min(частота,4).

## D-01 Коментар-виправдання

Місце: src/PrescriptionService.cs:54–66. Замість назв етапів доводиться звіряти 13 рядків пояснення з тілом Dispense. Категорія: читаність. Серйозність 3/5; частота 1; пріоритет 3. Гіпотеза: R01, R02.

```csharp
    // Відпуск за рецептом. Спочатку перевіряємо p:
    // чи рецепт є, чи не закритий і чи не минув строк.
    // Потім кількість: q - скільки просять, res2 -
    // залишок за рецептом (q = 0 - видати весь залишок).
    // Контрольовані: потрібен код провізора who, ліміт
    // MaxPerDispense за раз, без заміни, запис у журнал.
    // Не вистачає і x = true (згода на заміну) - беремо
    // дженерик з тією ж МНН; c - код того, що видаємо.
    // Списуємо партії (найближчий термін першим),
    // прострочені пропускаємо; n - скільки видали.
    // Видали менше, ніж треба - PartiallyDispensed.
    // k - пільга: пенсіонери 50%, інваліди безкоштовно.
    // Повертає суму; -1 - відмова (див. LastMessage).
```

## D-02 Довгий метод

Місце: src/PrescriptionService.cs:107–118. Dispense: 63 логічні рядки, CC=21, вкладеність=5. Поєднано перевірки, FEFO, статус і оплату. Категорія: змінюваність, тестованість. Серйозність 5/5; частота 1; пріоритет 5. Гіпотеза: R01.

```csharp
        if (x && StockOf(c) < q)
        {
            foreach (Medicine g in repo.Medicines)
            {
                if (g.Inn == m.Inn && g.Code != c)
                {
                    if (StockOf(g.Code) >= q)
                    {
                        p.History.Add("Generic " + g.Code);
                        c = g.Code;
                        break;
                    }
```

## D-03 Дублювання коду

Місце: src/InventoryService.cs:32–42. Дві родини клонів: залишок у трьох методах; формат рядка у двох методах ReportPrinter. Вікон по 6 рядків — п’ять; родин — дві. Категорія: змінюваність, надійність. Серйозність 4/5; частота 4; пріоритет 16. Гіпотеза: R10, R01.

```csharp
        int total = 0;
        foreach (StockBatch b in repo.Batches)
        {
            if (b.MedicineCode != code)
                continue;
            if (b.ExpiryDate < DateTime.Today)
                continue;
            total = total + b.Qty;
        }
        if (total + qty > 50)   // temporary limit until shelves are in the catalog
        {
```

## D-04 Довгий список параметрів

Місце: src/PrescriptionService.cs:14–25. Issue має 7 параметрів, дані пацієнта й рецепта легко переставити. Категорія: читаність, змінюваність. Серйозність 3/5; частота 1; пріоритет 3. Гіпотеза: R11 відкладено.

```csharp
    // days: 0 = стандартні 30 днів (контрольовані - 5)
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
```

## D-05 Одержимість елементарними типами

Місце: src/Prescription.cs:5–16. Category — довільний string; невідоме значення мовчки отримує звичайну ціну. Категорія: надійність. Серйозність 3/5; частота 4; пріоритет 12. Гіпотеза: R09 частково; тип категорії відкладено.

```csharp
// Рецепт ф. 1 (електронні теж вносимо вручну)
public class Prescription
{
    public string Number;
    public string PatientName;
    public string Category;          // "PENSIONER", "DISABLED" або ""
    public string Phone;
    public string MedicineCode;
    public int Quantity;             // упаковок за рецептом
    public int DispensedQty;
    public DateTime IssuedOn;
    public int ValidDays;
```

## D-06 Заздрість до чужих даних

Місце: src/ReportPrinter.cs:14–25. PrintReceipt читає 12 різних членів Prescription, власний стан лише repo. Категорія: зв’язність, змінюваність. Серйозність 3/5; частота 1; пріоритет 3. Гіпотеза: R01 частково.

```csharp
    {
        Prescription p = repo.FindPrescription(no);
        if (p == null)
            return;
        Medicine m = repo.FindMedicine(p.MedicineCode);
        Console.WriteLine("Receipt for " + p.PatientName + ", phone " + p.MaskedPhone());
        string line = p.Number + " | " + p.PatientName + " | " + p.MedicineCode;
        line = line + " | " + p.DispensedQty + "/" + p.Quantity;
        line = line + " | " + p.Status;
        if (p.Category != "")
            line = line + " | " + p.Category;
        if (p.Paid > 0)
```

## D-07 Тимчасове поле

Місце: src/ReportPrinter.cs:54–64. dayTotal потрібне лише одному виклику print_day, хоча зберігається у стані принтера. Категорія: читаність, надійність. Серйозність 2/5; частота 1; пріоритет 2. Гіпотеза: R15.

```csharp
    public void print_day()
    {
        dayTotal = 0m;
        int served = 0, open = 0, pens = 0, dis = 0;
        Console.WriteLine("Register:");
        foreach (Prescription p in repo.Prescriptions)
        {
            string line = p.Number + " | " + p.PatientName + " | " + p.MedicineCode;
            line = line + " | " + p.DispensedQty + "/" + p.Quantity;
            line = line + " | " + p.Status;
            if (p.Category != "")
```

## D-08 Магічні числа

Місце: src/PrescriptionService.cs:146–157. 0.5, 0.7, 30, 10; Receive додатково містить 90, 50, 1.25. Частоту обмежено чотирма місцями. Категорія: читаність, змінюваність. Серйозність 3/5; частота 4; пріоритет 12. Гіпотеза: R04, R09.

```csharp
        decimal k = 1m;
        switch (p.Category)
        {
            case "PENSIONER":
                k = 0.5m;
                if (DateTime.Today < new DateTime(2025, 4, 1))
                    k = 0.7m;
                break;
            case "DISABLED":
                k = 0m;
                break;
        }
```

## D-09 Проковтнутий виняток

Місце: src/AuditLog.cs:10–19. Будь-який збій File.AppendAllText приховано. Тест фіксує відсутність винятку, не підтверджує доставку аудиту. Категорія: надійність. Серйозність 4/5; частота 1; пріоритет 4. Гіпотеза: відкладено: зміна поведінки.

```csharp
    {
        if (!Enabled)
            return;
        try
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ";" + op + ";" + text;
            File.AppendAllText(FilePath, line + "\n");
        }
        catch { }
    }
```

## D-10 Мертвий код

Місце: src/PrescriptionService.cs:42–51. Закоментовані SmsGateway.Send і History SMS не виконуються; історія вже є в Git. Категорія: читаність. Серйозність 1/5; частота 1; пріоритет 1. Гіпотеза: R17.

```csharp
        p.Quantity = qty;
        p.IssuedOn = DateTime.Now;
        p.ValidDays = days;
        p.Doctor = doctor;
        p.History.Add("Issued");
        repo.Prescriptions.Add(p);
        // SmsGateway.Send(phone, "Rx " + p.Number + " registered");
        // p.History.Add("SMS");
        AuditLog.Write("ISSUE", p.Number + " " + code + " x" + qty);
        return p.Number;
```

## D-11 Надмірна вкладеність

Місце: src/PrescriptionService.cs:107–118. Пошук заміни вкладений у if + foreach + if + if, максимальна глибина методу 5. Категорія: читаність, тестованість. Серйозність 4/5; частота 1; пріоритет 4. Гіпотеза: R06.

```csharp
        if (x && StockOf(c) < q)
        {
            foreach (Medicine g in repo.Medicines)
            {
                if (g.Inn == m.Inn && g.Code != c)
                {
                    if (StockOf(g.Code) >= q)
                    {
                        p.History.Add("Generic " + g.Code);
                        c = g.Code;
                        break;
                    }
```

## D-12 Великий клас із відкритим станом

Місце: src/Prescription.cs:5–17. Prescription має 14 публічних полів; довільні зміни стану обходять сервіс. Відкрита колекція репозиторію — суміжний ризик. Категорія: інкапсуляція, надійність. Серйозність 4/5; частота 1; пріоритет 4. Гіпотеза: R13 частково; повний агрегат відкладено.

```csharp
// Рецепт ф. 1 (електронні теж вносимо вручну)
public class Prescription
{
    public string Number;
    public string PatientName;
    public string Category;          // "PENSIONER", "DISABLED" або ""
    public string Phone;
    public string MedicineCode;
    public int Quantity;             // упаковок за рецептом
    public int DispensedQty;
    public DateTime IssuedOn;
    public int ValidDays;
    public string Doctor;
```
