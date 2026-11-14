namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 提示文件名分配器测试
/// </summary>
public class HintNameAllocatorTest {

    #region Allocate

    /// <summary>
    /// 测试分配 - 短名后接 .g.cs 扩展名
    /// </summary>
    [Fact]
    public void TestAllocate_AppendsExtension() {
        var allocator = new HintNameAllocator();
        Assert.Equal( "Sample_Order.g.cs", allocator.Allocate( "Sample_Order" ) );
    }

    /// <summary>
    /// 测试分配 - 非法字符替换为下划线
    /// </summary>
    [Fact]
    public void TestAllocate_ReplacesIllegalCharacters() {
        var allocator = new HintNameAllocator();
        Assert.Equal( "Sample_Order_Item.g.cs", allocator.Allocate( "Sample.Order/Item" ) );
    }

    /// <summary>
    /// 测试分配 - 同名短名在扩展名前追加序号
    /// </summary>
    [Fact]
    public void TestAllocate_AppendsOrdinalOnCollision() {
        var allocator = new HintNameAllocator();
        Assert.Equal( "Order.g.cs", allocator.Allocate( "Order" ) );
        Assert.Equal( "Order_2.g.cs", allocator.Allocate( "Order" ) );
        Assert.Equal( "Order_3.g.cs", allocator.Allocate( "Order" ) );
    }

    /// <summary>
    /// 测试分配 - 净化后同名同样追加序号
    /// </summary>
    [Fact]
    public void TestAllocate_AppendsOrdinalAfterSanitize() {
        var allocator = new HintNameAllocator();
        Assert.Equal( "Sample_Order.g.cs", allocator.Allocate( "Sample.Order" ) );
        Assert.Equal( "Sample_Order_2.g.cs", allocator.Allocate( "Sample_Order" ) );
    }

    /// <summary>
    /// 测试分配 - 预留名被跳过
    /// </summary>
    [Fact]
    public void TestAllocate_SkipsReservedName() {
        var allocator = new HintNameAllocator( new[] { "Order", "Order_2" } );
        Assert.Equal( "Order_3.g.cs", allocator.Allocate( "Order" ) );
    }

    /// <summary>
    /// 测试分配 - 序号跳过已占用的同名后缀
    /// </summary>
    [Fact]
    public void TestAllocate_SkipsTakenSuffix() {
        var allocator = new HintNameAllocator( new[] { "Order_2" } );
        Assert.Equal( "Order.g.cs", allocator.Allocate( "Order" ) );
        Assert.Equal( "Order_3.g.cs", allocator.Allocate( "Order" ) );
    }

    /// <summary>
    /// 测试分配 - 保留中文等字母字符
    /// </summary>
    [Fact]
    public void TestAllocate_KeepsUnicodeLetters() {
        var allocator = new HintNameAllocator();
        Assert.Equal( "订单.g.cs", allocator.Allocate( "订单" ) );
    }

    /// <summary>
    /// 测试分配 - 短名为null抛出异常
    /// </summary>
    [Fact]
    public void TestAllocate_ThrowsOnNull() {
        var allocator = new HintNameAllocator();
        Assert.Throws<ArgumentNullException>( () => { allocator.Allocate( null ); } );
    }

    /// <summary>
    /// 测试分配 - 空短名抛出异常
    /// </summary>
    [Fact]
    public void TestAllocate_ThrowsOnEmpty() {
        var allocator = new HintNameAllocator();
        Assert.Throws<ArgumentException>( () => { allocator.Allocate( string.Empty ); } );
    }

    /// <summary>
    /// 测试分配 - 预留名同样净化后占用
    /// </summary>
    [Fact]
    public void TestAllocate_SanitizesReservedName() {
        var allocator = new HintNameAllocator( new[] { "Sample.Order" } );
        Assert.Equal( "Sample_Order_2.g.cs", allocator.Allocate( "Sample_Order" ) );
    }

    /// <summary>
    /// 测试分配 - 纯空白短名净化为下划线
    /// </summary>
    [Fact]
    public void TestAllocate_WhitespaceBecomesUnderscore() {
        var allocator = new HintNameAllocator();
        Assert.Equal( "_.g.cs", allocator.Allocate( " " ) );
    }

    /// <summary>
    /// 测试分配 - 预留完整提示文件名时同样跳过
    /// </summary>
    [Fact]
    public void TestAllocate_ReservedFullHintName_IsSkipped() {
        var allocator = new HintNameAllocator( new[] { "Order.g.cs" } );
        Assert.Equal( "Order_2.g.cs", allocator.Allocate( "Order" ) );
    }

    /// <summary>
    /// 测试分配 - 忽略空预留名
    /// </summary>
    [Fact]
    public void TestAllocate_IgnoresEmptyReservedNames() {
        var allocator = new HintNameAllocator( new string[] { "", null, "Order" } );
        Assert.Equal( "Order_2.g.cs", allocator.Allocate( "Order" ) );
    }

    #endregion
}
