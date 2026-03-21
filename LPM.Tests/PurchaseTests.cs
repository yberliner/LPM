using Microsoft.Data.Sqlite;
using LPM.Services;
using LPM.Tests.Helpers;
using Xunit;

namespace LPM.Tests;

/// <summary>
/// Tests for purchase workflow in <see cref="PcService"/>:
/// CreatePurchase, GetPurchases, GetPurchaseDetail, ApprovePurchase,
/// DeletePurchase, RestorePurchase, UpdatePurchase, GetPendingPurchasesForPc,
/// GetAllPurchasesForPc, SetMoneyInBank.
/// </summary>
public class PurchaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PcService _svc;

    public PurchaseTests()
    {
        _dbPath = TestDbHelper.CreateTempDb();
        _svc    = new PcService(TestConfig.For(_dbPath));
    }

    public void Dispose() => TestDbHelper.Cleanup(_dbPath);

    // =========================================================================
    // CreatePurchase
    // =========================================================================

    [Fact]
    public void CreatePurchase_ReturnsPositiveId()
    {
        var pcId = SetupPc("Alice", "A");
        var items = MakeItems(("Auditing", null, 10, 500));

        var id = _svc.CreatePurchase(pcId, "2024-06-01", "test notes", null, null, items);
        Assert.True(id > 0);
    }

    [Fact]
    public void CreatePurchase_InsertsItemsAndPaymentMethods()
    {
        var pcId = SetupPc("Bob", "B");
        var items = MakeItems(("Auditing", null, 5, 300), ("Auditing", null, 3, 200));
        var pms = MakePaymentMethods(("Cash", 400, "2024-06-01"), ("Check", 100, "2024-06-15"));

        var purchaseId = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items, pms);

        var detail = _svc.GetPurchaseDetail(purchaseId);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Items.Count);
        Assert.Equal(2, detail.PaymentMethods.Count);
    }

    [Fact]
    public void CreatePurchase_SetsDefaultPendingStatus()
    {
        var pcId = SetupPc("Carol", "C");
        var items = MakeItems(("Auditing", null, 1, 100));

        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal("Pending", detail!.ApprovedStatus);
    }

    [Fact]
    public void CreatePurchase_WithCourseItem_AutoEnrollsInStudentCourses()
    {
        var pcId = SetupPc("Dan", "D");
        using var conn = Open();
        var courseId = TestDbHelper.InsertCourse(conn, "Communication");

        var items = MakeItems(("Course", courseId, 0, 200));
        _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        // Verify auto-enrollment via CourseService
        var courseSvc = new CourseService(TestConfig.For(_dbPath));
        var courses = courseSvc.GetStudentCourses(pcId);
        Assert.Single(courses);
        Assert.Equal("Communication", courses[0].CourseName);
    }

    [Fact]
    public void CreatePurchase_InsertsItemInPurchaseItems()
    {
        var pcId = SetupPc("Eve", "E");
        var items = MakeItems(("Auditing", null, 10, 500));
        var purchaseId = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        using var conn = Open();
        var count = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM fin_purchase_items WHERE PurchaseId = {purchaseId}");
        Assert.Equal(1, count);
    }

    [Fact]
    public void CreatePurchase_WithSignature_StoresSignatureData()
    {
        var pcId = SetupPc("Fred", "F");
        var items = MakeItems(("Auditing", null, 1, 100));
        var sig = "data:image/png;base64,iVBORw0KGgo=";

        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, sig, null, items);
        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal(sig, detail!.SignatureData);
    }

    [Fact]
    public void CreatePurchase_WithCreatedBy_TracksCreator()
    {
        using var conn = Open();
        var pcId = SetupPc("Grace", "G");
        var staffId = TestDbHelper.InsertPerson(conn, "Staff", "User");

        var items = MakeItems(("Auditing", null, 5, 300));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, staffId, items);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.Contains("Staff", detail!.CreatedByName!);
    }

    [Fact]
    public void CreatePurchase_MultipleItems_DifferentTypes()
    {
        var pcId = SetupPc("Hank", "H");
        using var conn = Open();
        var courseId = TestDbHelper.InsertCourse(conn, "Life Repair");

        var items = MakeItems(
            ("Auditing", null, 10, 500),
            ("Course", courseId, 0, 300));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", "mixed purchase", null, null, items);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal(2, detail!.Items.Count);
        Assert.Contains(detail.Items, i => i.ItemType == "Auditing");
        Assert.Contains(detail.Items, i => i.ItemType == "Course");
    }

    // =========================================================================
    // GetPurchaseDetail
    // =========================================================================

    [Fact]
    public void GetPurchaseDetail_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_svc.GetPurchaseDetail(9999));
    }

    [Fact]
    public void GetPurchaseDetail_ReturnsCorrectPcName()
    {
        var pcId = SetupPc("Ivan", "Ivanov");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.Contains("Ivan", detail!.PcName);
        Assert.Contains("Ivanov", detail.PcName);
    }

    [Fact]
    public void GetPurchaseDetail_IncludesPaymentMethods()
    {
        var pcId = SetupPc("Jane", "J");
        var items = MakeItems(("Auditing", null, 5, 500));
        var pms = MakePaymentMethods(("Cash", 300, null), ("Check", 200, "2024-07-01"));

        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items, pms);
        var detail = _svc.GetPurchaseDetail(id);

        Assert.Equal(2, detail!.PaymentMethods.Count);
        Assert.Contains(detail.PaymentMethods, pm => pm.MethodType == "Cash" && pm.Amount == 300);
        Assert.Contains(detail.PaymentMethods, pm => pm.MethodType == "Check" && pm.Amount == 200);
    }

    [Fact]
    public void GetPurchaseDetail_ItemIncludesCourseName()
    {
        var pcId = SetupPc("Kate", "K");
        using var conn = Open();
        var courseId = TestDbHelper.InsertCourse(conn, "Objective Auditor");

        var items = MakeItems(("Course", courseId, 0, 200));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal("Objective Auditor", detail!.Items[0].CourseName);
    }

    // =========================================================================
    // GetPurchases (global list)
    // =========================================================================

    [Fact]
    public void GetPurchases_ReturnsEmpty_WhenNoPurchases()
    {
        Assert.Empty(_svc.GetPurchases(true));
    }

    [Fact]
    public void GetPurchases_ExcludesApproved_ByDefault()
    {
        var pcId = SetupPc("Leo", "L");
        var items = MakeItems(("Auditing", null, 5, 300));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        _svc.ApprovePurchase(id, pcId);

        var pending = _svc.GetPurchases(false);
        Assert.Empty(pending);

        var all = _svc.GetPurchases(true);
        Assert.Single(all);
    }

    [Fact]
    public void GetPurchases_ExcludesDeleted_ByDefault()
    {
        var pcId = SetupPc("Mia", "M");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        _svc.DeletePurchase(id);

        Assert.Empty(_svc.GetPurchases(true));
        Assert.Single(_svc.GetPurchases(true, includeDeleted: true));
    }

    [Fact]
    public void GetPurchases_FiltersByDateRange()
    {
        var pcId = SetupPc("Nina", "N");
        var items = MakeItems(("Auditing", null, 1, 100));
        _svc.CreatePurchase(pcId, "2024-01-15", null, null, null, items);
        _svc.CreatePurchase(pcId, "2024-06-15", null, null, null, items);
        _svc.CreatePurchase(pcId, "2024-12-15", null, null, null, items);

        var result = _svc.GetPurchases(true,
            from: new DateOnly(2024, 3, 1),
            to: new DateOnly(2024, 9, 30));
        Assert.Single(result);
        Assert.Equal("2024-06-15", result[0].PurchaseDate);
    }

    [Fact]
    public void GetPurchases_ShowsTotalAmountAndHours()
    {
        var pcId = SetupPc("Oscar", "O");
        var items = MakeItems(("Auditing", null, 10, 500), ("Auditing", null, 5, 300));
        _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        var list = _svc.GetPurchases(true);
        Assert.Single(list);
        Assert.Equal(800, list[0].TotalAmount);
        Assert.Equal(15, list[0].TotalHours);
    }

    // =========================================================================
    // ApprovePurchase
    // =========================================================================

    [Fact]
    public void ApprovePurchase_ChangesStatusToApproved()
    {
        var pcId = SetupPc("Paul", "P");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        _svc.ApprovePurchase(id, pcId);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal("Approved", detail!.ApprovedStatus);
        Assert.Contains("Paul", detail.ApprovedByName!);
    }

    // =========================================================================
    // DeletePurchase / RestorePurchase
    // =========================================================================

    [Fact]
    public void DeletePurchase_SoftDeletes()
    {
        var pcId = SetupPc("Quinn", "Q");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        _svc.DeletePurchase(id);

        // Detail should still be retrievable
        var detail = _svc.GetPurchaseDetail(id);
        Assert.NotNull(detail);

        // But not in the non-deleted list
        Assert.Empty(_svc.GetPurchases(true));
    }

    [Fact]
    public void RestorePurchase_UndoesDelete()
    {
        var pcId = SetupPc("Rosa", "R");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        _svc.DeletePurchase(id);
        Assert.Empty(_svc.GetPurchases(true));

        _svc.RestorePurchase(id);
        Assert.Single(_svc.GetPurchases(true));
    }

    // =========================================================================
    // UpdatePurchase
    // =========================================================================

    [Fact]
    public void UpdatePurchase_ChangesDateAndNotes()
    {
        var pcId = SetupPc("Sam", "S");
        var items = MakeItems(("Auditing", null, 5, 300));
        var pms = MakePaymentMethods(("Cash", 300, null));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", "old notes", null, null, items, pms);

        var newItems = MakeItems(("Auditing", null, 10, 600));
        var newPms = MakePaymentMethods(("Check", 600, "2024-07-01"));
        _svc.UpdatePurchase(id, "2024-07-01", "new notes", newItems, newPms);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal("2024-07-01", detail!.PurchaseDate);
        Assert.Equal("new notes", detail.Notes);
        Assert.Single(detail.Items);
        Assert.Equal(600, detail.Items[0].AmountPaid);
        Assert.Single(detail.PaymentMethods);
        Assert.Equal("Check", detail.PaymentMethods[0].MethodType);
    }

    [Fact]
    public void UpdatePurchase_ResetsStatusToPending()
    {
        var pcId = SetupPc("Tom", "T");
        var items = MakeItems(("Auditing", null, 5, 300));
        var pms = MakePaymentMethods(("Cash", 300, null));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items, pms);
        _svc.ApprovePurchase(id, pcId);

        _svc.UpdatePurchase(id, "2024-06-01", null, items, pms);
        var detail = _svc.GetPurchaseDetail(id);
        Assert.Equal("Pending", detail!.ApprovedStatus);
    }

    [Fact]
    public void UpdatePurchase_ReplacesItems()
    {
        var pcId = SetupPc("Uma", "U");
        var items = MakeItems(("Auditing", null, 5, 300));
        var pms = MakePaymentMethods(("Cash", 300, null));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items, pms);

        using var conn = Open();
        var before = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM fin_purchase_items WHERE PurchaseId = {id}");
        Assert.Equal(1, before);

        var newItems = MakeItems(("Auditing", null, 3, 200), ("Auditing", null, 7, 400));
        _svc.UpdatePurchase(id, "2024-06-01", null, newItems, pms);

        var after = TestDbHelper.Scalar(conn, $"SELECT COUNT(*) FROM fin_purchase_items WHERE PurchaseId = {id}");
        Assert.Equal(2, after);
    }

    // =========================================================================
    // GetPendingPurchasesForPc / GetAllPurchasesForPc
    // =========================================================================

    [Fact]
    public void GetPendingPurchasesForPc_ReturnsOnlyPending()
    {
        var pcId = SetupPc("Vera", "V");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id1 = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        var id2 = _svc.CreatePurchase(pcId, "2024-06-15", null, null, null, items);
        _svc.ApprovePurchase(id1, pcId);

        var pending = _svc.GetPendingPurchasesForPc(pcId);
        Assert.Single(pending);
        Assert.Equal(id2, pending[0].PurchaseId);
    }

    [Fact]
    public void GetPendingPurchasesForPc_ExcludesDeleted()
    {
        var pcId = SetupPc("Will", "W");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        _svc.DeletePurchase(id);

        Assert.Empty(_svc.GetPendingPurchasesForPc(pcId));
    }

    [Fact]
    public void GetAllPurchasesForPc_ReturnsAllNonDeleted()
    {
        var pcId = SetupPc("Xena", "X");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id1 = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        var id2 = _svc.CreatePurchase(pcId, "2024-06-15", null, null, null, items);
        _svc.ApprovePurchase(id1, pcId);

        var all = _svc.GetAllPurchasesForPc(pcId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetAllPurchasesForPc_ExcludesDeleted()
    {
        var pcId = SetupPc("Yuri", "Y");
        var items = MakeItems(("Auditing", null, 1, 100));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);
        _svc.DeletePurchase(id);

        Assert.Empty(_svc.GetAllPurchasesForPc(pcId));
    }

    [Fact]
    public void GetAllPurchasesForPc_OrderedByDateDesc()
    {
        var pcId = SetupPc("Zara", "Z");
        var items = MakeItems(("Auditing", null, 1, 100));
        _svc.CreatePurchase(pcId, "2024-01-01", null, null, null, items);
        _svc.CreatePurchase(pcId, "2024-12-01", null, null, null, items);
        _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items);

        var all = _svc.GetAllPurchasesForPc(pcId);
        Assert.Equal("2024-12-01", all[0].PurchaseDate);
        Assert.Equal("2024-06-01", all[1].PurchaseDate);
        Assert.Equal("2024-01-01", all[2].PurchaseDate);
    }

    // =========================================================================
    // SetMoneyInBank
    // =========================================================================

    [Fact]
    public void SetMoneyInBank_MarksPaymentMethodAsInBank()
    {
        var pcId = SetupPc("Ann", "AB");
        var items = MakeItems(("Auditing", null, 5, 500));
        var pms = MakePaymentMethods(("Cash", 500, "2024-06-01"));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items, pms);

        var detail = _svc.GetPurchaseDetail(id);
        var pmId = detail!.PaymentMethods[0].PaymentMethodId;

        Assert.False(detail.PaymentMethods[0].IsMoneyInBank);

        _svc.SetMoneyInBank(pmId, true);

        detail = _svc.GetPurchaseDetail(id);
        Assert.True(detail!.PaymentMethods[0].IsMoneyInBank);
        Assert.NotNull(detail.PaymentMethods[0].MoneyInBankDate);
    }

    [Fact]
    public void SetMoneyInBank_CanUnmark()
    {
        var pcId = SetupPc("Ben", "BC");
        var items = MakeItems(("Auditing", null, 5, 500));
        var pms = MakePaymentMethods(("Cash", 500, "2024-06-01"));
        var id = _svc.CreatePurchase(pcId, "2024-06-01", null, null, null, items, pms);

        var pmId = _svc.GetPurchaseDetail(id)!.PaymentMethods[0].PaymentMethodId;

        _svc.SetMoneyInBank(pmId, true);
        _svc.SetMoneyInBank(pmId, false);

        var detail = _svc.GetPurchaseDetail(id);
        Assert.False(detail!.PaymentMethods[0].IsMoneyInBank);
        Assert.Null(detail.PaymentMethods[0].MoneyInBankDate);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private int SetupPc(string firstName, string lastName)
    {
        using var conn = Open();
        var pid = TestDbHelper.InsertPerson(conn, firstName, lastName);
        TestDbHelper.InsertPC(conn, pid);
        return pid;
    }

    private static List<(string itemType, int? courseId, int hoursBought, int amountPaid)>
        MakeItems(params (string type, int? courseId, int hours, int amount)[] raw)
    {
        return raw.Select(r => (r.type, r.courseId, r.hours, r.amount)).ToList();
    }

    private static List<(string methodType, int amount, string? paymentDate)>
        MakePaymentMethods(params (string type, int amount, string? date)[] raw)
    {
        return raw.Select(r => (r.type, r.amount, r.date)).ToList();
    }
}
