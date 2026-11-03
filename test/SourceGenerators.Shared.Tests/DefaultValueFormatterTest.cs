namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 默认值字面量输出测试
/// </summary>
public class DefaultValueFormatterTest {
    /// <summary>
    /// ulong 极值枚举不抛异常并输出无符号字面量
    /// </summary>
    [Fact]
    public void TestToInteger_UlongExtreme_DoesNotThrow() {
        var type = GetExtremeEnum();
        var result = DefaultValueFormatter.FormatValue( 18446744073709551615UL, type );
        Assert.Equal( "( global::Probe.Extreme )18446744073709551615", result );
    }

    /// <summary>
    /// ulong 范围内的普通枚举值保持原有输出
    /// </summary>
    [Fact]
    public void TestToInteger_SmallEnumValue_Unchanged() {
        var type = GetExtremeEnum();
        var result = DefaultValueFormatter.FormatValue( 5UL, type );
        Assert.Equal( "( global::Probe.Extreme )5", result );
    }

    /// <summary>
    /// NaN 与正负无穷按目标类型输出静态成员
    /// </summary>
    [Fact]
    public void TestFloating_InfinityLiterals() {
        var compilation = CreateCompilation();
        var floatType = compilation.GetSpecialType( SpecialType.System_Single );
        var doubleType = compilation.GetSpecialType( SpecialType.System_Double );
        Assert.Equal( "float.NaN", DefaultValueFormatter.FormatValue( float.NaN, floatType ) );
        Assert.Equal( "float.PositiveInfinity", DefaultValueFormatter.FormatValue( float.PositiveInfinity, floatType ) );
        Assert.Equal( "float.NegativeInfinity", DefaultValueFormatter.FormatValue( float.NegativeInfinity, floatType ) );
        Assert.Equal( "double.NaN", DefaultValueFormatter.FormatValue( double.NaN, doubleType ) );
        Assert.Equal( "double.PositiveInfinity", DefaultValueFormatter.FormatValue( double.PositiveInfinity, doubleType ) );
        Assert.Equal( "double.NegativeInfinity", DefaultValueFormatter.FormatValue( double.NegativeInfinity, doubleType ) );
    }

    /// <summary>
    /// 常用字面量按目标类型输出
    /// </summary>
    [Fact]
    public void TestFormatValue_CommonLiterals() {
        var compilation = CreateCompilation();
        var intType = compilation.GetSpecialType( SpecialType.System_Int32 );
        var boolType = compilation.GetSpecialType( SpecialType.System_Boolean );
        var stringType = compilation.GetSpecialType( SpecialType.System_String );
        var floatType = compilation.GetSpecialType( SpecialType.System_Single );
        Assert.Equal( "42", DefaultValueFormatter.FormatValue( 42, intType ) );
        Assert.Equal( "true", DefaultValueFormatter.FormatValue( true, boolType ) );
        Assert.Equal( "\"abc\"", DefaultValueFormatter.FormatValue( "abc", stringType ) );
        Assert.Equal( "float.NaN", DefaultValueFormatter.FormatValue( float.NaN, floatType ) );
    }

    /// <summary>
    /// 参数默认值按 default 与 null 区分输出
    /// </summary>
    [Fact]
    public void TestFormat_ParameterDefaults() {
        var compilation = TestCompilation.Create( @"
            namespace Probe {
                public struct Sample { }
                public class Host {
                    public void Run( int count = 42, string name = null, Sample sample = default ) { }
                }
            }" );
        var host = compilation.GetTypeByMetadataName( "Probe.Host" );
        var method = host.GetMembers( "Run" ).OfType<IMethodSymbol>().Single();
        Assert.Equal( "42", DefaultValueFormatter.Format( method.Parameters[0] ) );
        Assert.Equal( "null", DefaultValueFormatter.Format( method.Parameters[1] ) );
        Assert.Equal( "default", DefaultValueFormatter.Format( method.Parameters[2] ) );
    }

    /// <summary>
    /// 创建包含 ulong 极值枚举的桩编译单元
    /// </summary>
    private static CSharpCompilation CreateCompilation() {
        return TestCompilation.Create( @"
            namespace Probe;

            public enum Extreme : ulong {
                Zero = 0,
                Max = 18446744073709551615
            }" );
    }

    /// <summary>
    /// 获取 ulong 极值枚举符号
    /// </summary>
    private static INamedTypeSymbol GetExtremeEnum() {
        return (INamedTypeSymbol)CreateCompilation().GetTypeByMetadataName( "Probe.Extreme" );
    }
}
