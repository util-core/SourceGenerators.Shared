namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 类型名字符串处理测试
/// </summary>
public class TypeNameTextTest {
    /// <summary>
    /// 去掉与补全 global:: 前缀
    /// </summary>
    [Fact]
    public void TestGlobalPrefix() {
        Assert.Equal( "A.B", TypeNameText.StripGlobal( "global::A.B" ) );
        Assert.Equal( "global::A.B", TypeNameText.Qualify( "A.B" ) );
        Assert.Equal( "global::A.B", TypeNameText.Qualify( "global::A.B" ) );
    }

    /// <summary>
    /// 数组后缀按维数生成,一维为[]多维补逗号
    /// </summary>
    [Fact]
    public void TestArraySuffix() {
        Assert.Equal( "[]", TypeNameText.ArraySuffix( 1 ) );
        Assert.Equal( "[,]", TypeNameText.ArraySuffix( 2 ) );
        Assert.Equal( "[,,]", TypeNameText.ArraySuffix( 3 ) );
    }

    /// <summary>
    /// 分离多层数组后缀
    /// </summary>
    [Fact]
    public void TestSplitArraySuffix() {
        Assert.Equal( "List<int>", TypeNameText.SplitArraySuffix( "List<int>[][]", out var suffix ) );
        Assert.Equal( "[][]", suffix );
        Assert.Equal( "List<int>", TypeNameText.SplitArraySuffix( "List<int>", out var none ) );
        Assert.Equal( string.Empty, none );
    }

    /// <summary>
    /// 按顶层逗号拆分泛型实参
    /// </summary>
    [Fact]
    public void TestSplitTopLevel() {
        var items = TypeNameText.SplitTopLevel( "Dictionary<string, int>, List<int>" ).ToArray();
        Assert.Equal( new[] { "Dictionary<string, int>", "List<int>" }, items );
    }

    /// <summary>
    /// 查找匹配的右尖括号
    /// </summary>
    [Fact]
    public void TestFindMatchingAngle() {
        const string text = "List<Dictionary<string, int>>";
        Assert.Equal( text.Length - 1, TypeNameText.FindMatchingAngle( text, 4 ) );
        Assert.Equal( -1, TypeNameText.FindMatchingAngle( "List<int", 4 ) );
    }

    /// <summary>
    /// 测试顶层拆分忽略数组秩内的逗号
    /// </summary>
    [Fact]
    public void TestSplitTopLevel_ArrayRankComma() {
        var items = TypeNameText.SplitTopLevel( "Sample.Grid[,], int" ).ToArray();
        Assert.Equal( new[] { "Sample.Grid[,]", "int" }, items );
    }

    /// <summary>
    /// 测试分离多维与交错数组后缀
    /// </summary>
    [Fact]
    public void TestSplitArraySuffix_Multidimensional() {
        Assert.Equal( "List<int>", TypeNameText.SplitArraySuffix( "List<int>[,][]", out var suffix ) );
        Assert.Equal( "[,][]", suffix );
    }

    /// <summary>
    /// 测试泛型实参含多维数组时输出规范文本
    /// </summary>
    [Fact]
    public void TestReference_GenericWithMultidimensionalArray() {
        Assert.Equal( "Dictionary<Grid[,], int>",
            GeneratedSource.Reference( "Sample.Dictionary<Sample.Grid[,], int>", new[] { "Sample" } ) );
    }
}
