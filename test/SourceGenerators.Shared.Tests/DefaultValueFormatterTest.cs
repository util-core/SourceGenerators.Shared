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

    /// <summary>
    /// 可空枚举参数默认值保留枚举转换
    /// </summary>
    [Fact]
    public void TestFormat_NullableEnumDefault_KeepsEnumCast() {
        var compilation = TestCompilation.Create( @"
            namespace Sample {
                public enum Level { Low = 1, High = 2 }
                public class Host {
                    public void Run( Level? level = Level.High ) { }
                }
            }" );
        var method = compilation.GetTypeByMetadataName( "Sample.Host" ).GetMembers( "Run" ).OfType<IMethodSymbol>().Single();
        Assert.Equal( "( global::Sample.Level )2", DefaultValueFormatter.Format( method.Parameters[0] ) );
    }

    /// <summary>
    /// 可空类型的配置文本按基础类型解析
    /// </summary>
    [Fact]
    public void TestFormat_NullableText_ParsesUnderlyingType() {
        var compilation = TestCompilation.Create( @"
            namespace Sample {
                public enum Level { Low = 1, High = 2 }
            }" );
        var nullableDefinition = compilation.GetSpecialType( SpecialType.System_Nullable_T );
        var nullableLevel = nullableDefinition.Construct( compilation.GetTypeByMetadataName( "Sample.Level" ) );
        var nullableInt = nullableDefinition.Construct( compilation.GetSpecialType( SpecialType.System_Int32 ) );
        Assert.Equal( "( global::Sample.Level )2", DefaultValueFormatter.Format( "High", nullableLevel ) );
        Assert.Equal( "5", DefaultValueFormatter.Format( "5", nullableInt ) );
    }

    /// <summary>
    /// 字符配置文本必须且只能包含一个字符
    /// </summary>
    [Fact]
    public void TestFormat_Char_RejectsMultipleCharacters() {
        var compilation = CreateCompilation();
        var charType = compilation.GetSpecialType( SpecialType.System_Char );
        Assert.Equal( "'a'", DefaultValueFormatter.Format( "a", charType ) );
        Assert.Null( DefaultValueFormatter.Format( "ab", charType ) );
    }

    /// <summary>
    /// 字符串字面量完整转义控制字符与行分隔符
    /// </summary>
    [Fact]
    public void TestQuote_EscapesControlAndLineSeparators() {
        Assert.Equal( "\"a\\\"b\\\\c\\n\\r\\t\\0\"", DefaultValueFormatter.Quote( "a\"b\\c\n\r\t\0" ) );
        Assert.Equal( "\"\\u2028\\u2029\"", DefaultValueFormatter.Quote( "\u2028\u2029" ) );
        Assert.Equal( "\"\\uD800\"", DefaultValueFormatter.Quote( "\uD800" ) );
    }

    /// <summary>
    /// 字符字面量转义单引号与控制字符
    /// </summary>
    [Fact]
    public void TestFormatValue_CharEscapes() {
        var compilation = CreateCompilation();
        var charType = compilation.GetSpecialType( SpecialType.System_Char );
        Assert.Equal( "'\\''", DefaultValueFormatter.FormatValue( '\'', charType ) );
        Assert.Equal( "'\"'", DefaultValueFormatter.FormatValue( '"', charType ) );
        Assert.Equal( "'\\u0001'", DefaultValueFormatter.FormatValue( '\u0001', charType ) );
    }

    /// <summary>
    /// null 配置文本按省略处理
    /// </summary>
    [Fact]
    public void TestFormat_NullText_IsOmitted() {
        var compilation = CreateCompilation();
        var stringType = compilation.GetSpecialType( SpecialType.System_String );
        Assert.Equal( DefaultValueFormatter.Omitted, DefaultValueFormatter.Format( (string)null, stringType ) );
    }

    /// <summary>
    /// 特性实参输出 typeof 与数组字面量
    /// </summary>
    [Fact]
    public void TestFormat_TypedConstant_TypeAndArray() {
        var compilation = TestCompilation.Create( @"
            namespace Probe {
                public class SampleAttribute : System.Attribute {
                    public SampleAttribute( System.Type type, int[] values ) { }
                }
                [Sample( typeof( string ), new[] { 1, 2 } )]
                public class Customer { }
            }" );
        var attribute = compilation.GetTypeByMetadataName( "Probe.Customer" ).GetAttributes().Single();
        Assert.Equal( "typeof( string )", DefaultValueFormatter.Format( attribute.ConstructorArguments[0] ) );
        Assert.Equal( "new int[] { 1, 2 }", DefaultValueFormatter.Format( attribute.ConstructorArguments[1] ) );
    }

    /// <summary>
    /// null 特性实参输出 null
    /// </summary>
    [Fact]
    public void TestFormat_TypedConstant_Null() {
        var compilation = TestCompilation.Create( @"
            namespace Probe {
                public class SampleAttribute : System.Attribute {
                    public SampleAttribute( System.Type type ) { }
                }
                [Sample( null )]
                public class Customer { }
            }" );
        var attribute = compilation.GetTypeByMetadataName( "Probe.Customer" ).GetAttributes().Single();
        Assert.Equal( "null", DefaultValueFormatter.Format( attribute.ConstructorArguments[0] ) );
    }

    /// <summary>
    /// 整数、浮点与 decimal 输出对应后缀
    /// </summary>
    [Fact]
    public void TestFormatValue_NumericSuffixes() {
        var compilation = CreateCompilation();
        Assert.Equal( "1.5m", DefaultValueFormatter.FormatValue( 1.5m, compilation.GetSpecialType( SpecialType.System_Decimal ) ) );
        Assert.Equal( "5L", DefaultValueFormatter.FormatValue( 5L, compilation.GetSpecialType( SpecialType.System_Int64 ) ) );
        Assert.Equal( "5UL", DefaultValueFormatter.FormatValue( 5UL, compilation.GetSpecialType( SpecialType.System_UInt64 ) ) );
        Assert.Equal( "5U", DefaultValueFormatter.FormatValue( 5U, compilation.GetSpecialType( SpecialType.System_UInt32 ) ) );
        Assert.Equal( "5", DefaultValueFormatter.FormatValue( (byte)5, compilation.GetSpecialType( SpecialType.System_Byte ) ) );
    }

    /// <summary>
    /// 配置文本解析失败返回 null
    /// </summary>
    [Fact]
    public void TestFormat_InvalidText_ReturnsNull() {
        var compilation = CreateCompilation();
        var intType = compilation.GetSpecialType( SpecialType.System_Int32 );
        Assert.Equal( "42", DefaultValueFormatter.Format( "42", intType ) );
        Assert.Null( DefaultValueFormatter.Format( "abc", intType ) );
        Assert.Equal( "true", DefaultValueFormatter.Format( "true", compilation.GetSpecialType( SpecialType.System_Boolean ) ) );
    }
}
