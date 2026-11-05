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
}
