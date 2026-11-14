namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 类型引用作用域测试
/// </summary>
public class ImportScopeTest {
    /// <summary>
    /// 冲突命名空间桩源码
    /// </summary>
    private const string ConflictSource = @"
namespace Lib.A { public class Widget { } public class HelperA { } }
namespace Lib.B { public class Widget { } public class HelperB { } }";

    /// <summary>
    /// 已导入命名空间的类型用短名
    /// </summary>
    [Fact]
    public void TestReference_ShortensImportedType() {
        var compilation = TestCompilation.Create( @"namespace Sample.Domain { public class Customer { } }" );
        var type = compilation.GetTypeByMetadataName( "Sample.Domain.Customer" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { type } );
        Assert.Contains( "Sample.Domain", scope.Imports );
        Assert.Equal( "Customer", scope.Reference( type ) );
    }

    /// <summary>
    /// 无重名类型的命名空间不互斥,全部保留
    /// </summary>
    [Fact]
    public void TestImports_IndependentNamespaces_AreBothImported() {
        var compilation = TestCompilation.Create( @"namespace Sample.A { public class A { } } namespace Sample.B { public class B { } }" );
        var a = compilation.GetTypeByMetadataName( "Sample.A.A" );
        var b = compilation.GetTypeByMetadataName( "Sample.B.B" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( "Sample.A", scope.Imports );
        Assert.Contains( "Sample.B", scope.Imports );
        Assert.DoesNotContain( scope.Imports, item => item.Contains( " = " ) );
        Assert.Equal( "A", scope.Reference( a ) );
        Assert.Equal( "B", scope.Reference( b ) );
    }

    /// <summary>
    /// 对外构造签名只保留编译单元、框架清单与类型清单三个参数,私有派生构造固定为作用域与文件命名空间两参
    /// </summary>
    [Fact]
    public void TestConstructor_NoConflictingNamespacesParameter() {
        var constructors = typeof( ImportScope ).GetConstructors( System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic );
        Assert.NotEmpty( constructors );
        Assert.All( constructors.Where( item => item.IsPrivate == false ), item => Assert.Equal( 3, item.GetParameters().Length ) );
        Assert.All( constructors.Where( item => item.IsPrivate ), item => Assert.Equal( 2, item.GetParameters().Length ) );
    }

    /// <summary>
    /// 嵌套类型提取所属命名空间,引用使用嵌套链短名
    /// </summary>
    [Fact]
    public void TestReference_NestedType_ExtractsNamespace() {
        var compilation = TestCompilation.Create( @"namespace Lib.Samples { public class OuterEntity { public class InnerEntity { } } }" );
        var inner = GetNestedType( compilation, "Lib.Samples.OuterEntity", "InnerEntity" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { inner } );
        Assert.Contains( "Lib.Samples", scope.Imports );
        Assert.Equal( "OuterEntity.InnerEntity", scope.Reference( inner ) );
    }

    /// <summary>
    /// 两个命名空间的嵌套链首段同名时保留优先命名空间,被排除侧保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_NestedType_SameOuterName_KeepsPreferredNamespace() {
        var compilation = TestCompilation.Create(
            @"namespace Lib.Conflict.A { public class Wrapper { public class Item { } } }",
            @"namespace Lib.Conflict.B { public class Wrapper { public class Item { } } }" );
        var a = GetNestedType( compilation, "Lib.Conflict.A.Wrapper", "Item" );
        var b = GetNestedType( compilation, "Lib.Conflict.B.Wrapper", "Item" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( "Lib.Conflict.A", scope.Imports );
        Assert.DoesNotContain( "Lib.Conflict.B", scope.Imports );
        Assert.Equal( "Wrapper.Item", scope.Reference( a ) );
        Assert.Equal( "global::Lib.Conflict.B.Wrapper.Item", scope.Reference( b ) );
    }

    /// <summary>
    /// 嵌套链首段与顶层类型同名时保留优先命名空间,被排除侧使用别名
    /// </summary>
    [Fact]
    public void TestReference_NestedType_OuterNameCollidesTopLevel_UsesAlias() {
        var compilation = TestCompilation.Create(
            @"namespace Lib.Samples { public class OuterEntity { public class InnerEntity { } } }",
            @"namespace Sample.Isolated { public class OuterEntity { } }" );
        var inner = GetNestedType( compilation, "Lib.Samples.OuterEntity", "InnerEntity" );
        var top = compilation.GetTypeByMetadataName( "Sample.Isolated.OuterEntity" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { inner, top } );
        Assert.Contains( "Lib.Samples", scope.Imports );
        Assert.DoesNotContain( "Sample.Isolated", scope.Imports );
        Assert.Equal( "OuterEntity.InnerEntity", scope.Reference( inner ) );
        Assert.Equal( "SampleIsolatedOuterEntity", scope.Reference( top ) );
    }

    /// <summary>
    /// 全限定名字符串按导入清单短名化
    /// </summary>
    [Fact]
    public void TestReference_StringOverload() {
        var compilation = TestCompilation.Create( @"namespace Sample.Domain { public class Customer { } }" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Sample.Domain.Customer" } );
        Assert.Contains( "Sample.Domain", scope.Imports );
        Assert.Equal( "Customer", scope.Reference( "Sample.Domain.Customer" ) );
    }

    /// <summary>
    /// 数组与泛型实参递归缩短
    /// </summary>
    [Fact]
    public void TestReference_ArrayAndGeneric() {
        var compilation = TestCompilation.Create( @"namespace Sample.Domain { public class Customer { } }" );
        var customer = compilation.GetTypeByMetadataName( "Sample.Domain.Customer" );
        var array = compilation.CreateArrayTypeSymbol( customer );
        var listDefinition = compilation.GetTypeByMetadataName( "System.Collections.Generic.List`1" );
        var list = listDefinition.Construct( customer );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { array, list } );
        Assert.Contains( "Sample.Domain", scope.Imports );
        Assert.Contains( "System.Collections.Generic", scope.Imports );
        Assert.Equal( "Customer[]", scope.Reference( array ) );
        Assert.Equal( "List<Customer>", scope.Reference( list ) );
    }

    /// <summary>
    /// 多维数组引用按维数补逗号,交错数组逐层保留方括号
    /// </summary>
    [Fact]
    public void TestReference_MultidimensionalArray() {
        var compilation = TestCompilation.Create( @"namespace Sample.Domain { public class Customer { } }" );
        var customer = compilation.GetTypeByMetadataName( "Sample.Domain.Customer" );
        var multi = compilation.CreateArrayTypeSymbol( customer, 2 );
        var jagged = compilation.CreateArrayTypeSymbol( compilation.CreateArrayTypeSymbol( customer ), 1 );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { multi, jagged } );
        Assert.Equal( "Customer[,]", scope.Reference( multi ) );
        Assert.Equal( "Customer[][]", scope.Reference( jagged ) );
    }

    /// <summary>
    /// 未参与建域的类型已导入时用短名,否则保留全限定名
    /// </summary>
    [Fact]
    public void TestReferenceExternal_UsesShortNameWhenImported() {
        var compilation = TestCompilation.Create( @"namespace Sample.Domain { public class Customer { } }" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Sample.Domain.Customer" } );
        Assert.Equal( "Customer", scope.ReferenceExternal( "Sample.Domain.Customer" ) );
        Assert.Equal( "global::Sample.Other.Missing", scope.ReferenceExternal( "Sample.Other.Missing" ) );
    }

    /// <summary>
    /// 获取嵌套类型符号
    /// </summary>
    private static INamedTypeSymbol GetNestedType( Compilation compilation, string outerName, string innerName ) {
        var outer = compilation.GetTypeByMetadataName( outerName );
        return outer.GetTypeMembers( innerName ).Single();
    }

    /// <summary>
    /// 构建泛型外层下的嵌套泛型类型符号
    /// </summary>
    private static INamedTypeSymbol ConstructNested( Compilation compilation, string outerName, string innerName,
            SpecialType outerArgument, SpecialType innerArgument ) {
        var outer = ( (INamedTypeSymbol)compilation.GetTypeByMetadataName( outerName ) )
            .Construct( compilation.GetSpecialType( outerArgument ) );
        var inner = outer.GetTypeMembers( innerName ).Single();
        return inner.Construct( compilation.GetSpecialType( innerArgument ) );
    }

    /// <summary>
    /// 泛型外层嵌套类型整体缩短
    /// </summary>
    [Fact]
    public void TestReference_NestedGeneric_GenericOuter_Shortened() {
        var compilation = TestCompilation.Create( "namespace Lib { public class Outer<T> { public class Inner<U> { } } }" );
        var inner = ConstructNested( compilation, "Lib.Outer`1", "Inner", SpecialType.System_Int32, SpecialType.System_String );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { inner } );
        Assert.Contains( "Lib", scope.Imports );
        Assert.Equal( "Outer<int>.Inner<string>", scope.Reference( inner ) );
    }

    /// <summary>
    /// 外层泛型歧义时保留优先命名空间,被排除侧保留全限定名且不生成别名
    /// </summary>
    [Fact]
    public void TestReference_NestedGeneric_AmbiguousOuter_KeepsPreferredNamespace() {
        var compilation = TestCompilation.Create(
            @"namespace Lib.A { public class Outer<T> { public class Inner<U> { } } }",
            @"namespace Lib.B { public class Outer<T> { public class Inner<U> { } } }" );
        var a = ConstructNested( compilation, "Lib.A.Outer`1", "Inner", SpecialType.System_Int32, SpecialType.System_String );
        var b = ConstructNested( compilation, "Lib.B.Outer`1", "Inner", SpecialType.System_Int32, SpecialType.System_String );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( "Lib.A", scope.Imports );
        Assert.DoesNotContain( "Lib.B", scope.Imports );
        Assert.Equal( "Outer<int>.Inner<string>", scope.Reference( a ) );
        Assert.Equal( "global::Lib.B.Outer<int>.Inner<string>", scope.Reference( b ) );
    }

    /// <summary>
    /// 嵌套类型链中的关键字使用 @ 转义
    /// </summary>
    [Fact]
    public void TestReference_NestedType_EscapesKeywords() {
        var compilation = TestCompilation.Create( "namespace Lib { public class @class { public class @int { } } }" );
        var inner = GetNestedType( compilation, "Lib.class", "int" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { inner } );
        Assert.Equal( "@class.@int", scope.Reference( inner ) );
    }

    /// <summary>
    /// System 子命名空间未显式导入时保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_SystemSubNamespace_RequiresExplicitImport() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        var builder = compilation.GetTypeByMetadataName( "System.Text.StringBuilder" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Sample.Customer" } );
        Assert.Equal( "global::System.Text.StringBuilder", scope.Reference( builder ) );
        Assert.Equal( "global::System.Text.StringBuilder", scope.ReferenceExternal( "System.Text.StringBuilder" ) );
    }

    /// <summary>
    /// System 子命名空间显式导入后使用短名
    /// </summary>
    [Fact]
    public void TestReference_SystemSubNamespace_ShortensWhenImported() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        var builder = compilation.GetTypeByMetadataName( "System.Text.StringBuilder" );
        var scope = new ImportScope( compilation, new[] { "System", "System.Text" }, new[] { "Sample.Customer" } );
        Assert.Equal( "StringBuilder", scope.Reference( builder ) );
        Assert.Equal( "StringBuilder", scope.ReferenceExternal( "System.Text.StringBuilder" ) );
    }

    /// <summary>
    /// ReferenceExternal 支持泛型与数组
    /// </summary>
    [Fact]
    public void TestReferenceExternal_GenericAndArray() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        var scope = new ImportScope( compilation, new[] { "System", "System.Collections.Generic" }, new[] { "Sample.Customer" } );
        Assert.Equal( "List<String>", scope.ReferenceExternal( "System.Collections.Generic.List<System.String>" ) );
        Assert.Equal( "List<String>[]", scope.ReferenceExternal( "System.Collections.Generic.List<System.String>[]" ) );
    }

    /// <summary>
    /// 字符串全名中的嵌套泛型整体缩短
    /// </summary>
    [Fact]
    public void TestReference_String_NestedGeneric_Shortened() {
        var compilation = TestCompilation.Create(
            @"namespace Lib { public class Outer<T> { public class Inner<U> { } } }",
            @"namespace Sample { public class Customer { } }" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Lib.Outer<int>.Inner<string>", "Sample.Customer" } );
        Assert.Contains( "Lib", scope.Imports );
        Assert.Equal( "Outer<int>.Inner<string>", scope.Reference( "Lib.Outer<int>.Inner<string>" ) );
    }

    /// <summary>
    /// 字符串全名支持数组后缀
    /// </summary>
    [Fact]
    public void TestReference_String_Array() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Sample.Customer" } );
        Assert.Equal( "Customer[]", scope.Reference( "Sample.Customer[]" ) );
    }

    /// <summary>
    /// 未导入命名空间的字符串全名补 global:: 限定
    /// </summary>
    [Fact]
    public void TestReference_String_QualifiesUnimportedNamespace() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Sample.Customer" } );
        Assert.Equal( "global::Other.Ns.Type", scope.Reference( "Other.Ns.Type" ) );
    }

    /// <summary>
    /// 全局命名空间类型补 global:: 限定
    /// </summary>
    [Fact]
    public void TestReference_GlobalType_Qualified() {
        var compilation = TestCompilation.Create( "public class GlobalCustomer { }" );
        var type = compilation.GetTypeByMetadataName( "GlobalCustomer" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { type } );
        Assert.Equal( "global::GlobalCustomer", scope.Reference( type ) );
    }

    /// <summary>
    /// 与框架命名空间同名时排除业务命名空间并使用别名
    /// </summary>
    [Fact]
    public void TestReference_FrameworkNameCollision_UsesAlias() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Object { } }" );
        var type = compilation.GetTypeByMetadataName( "Sample.Object" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { type } );
        Assert.DoesNotContain( "Sample", scope.Imports );
        Assert.Contains( "SampleObject = global::Sample.Object", scope.Imports );
        Assert.Equal( "SampleObject", scope.Reference( type ) );
    }

    /// <summary>
    /// 跨命名空间顶层同名类型保留优先者,被排除侧使用别名
    /// </summary>
    [Fact]
    public void TestReference_TopLevelNameConflict_UsesAlias() {
        var compilation = TestCompilation.Create(
            @"namespace Sample.A { public class Customer { } }",
            @"namespace Sample.B { public class Customer { } }" );
        var a = compilation.GetTypeByMetadataName( "Sample.A.Customer" );
        var b = compilation.GetTypeByMetadataName( "Sample.B.Customer" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( "Sample.A", scope.Imports );
        Assert.DoesNotContain( "Sample.B", scope.Imports );
        Assert.Contains( "SampleBCustomer = global::Sample.B.Customer", scope.Imports );
        Assert.Equal( "Customer", scope.Reference( a ) );
        Assert.Equal( "SampleBCustomer", scope.Reference( b ) );
    }

    /// <summary>
    /// 短名引用结果可直接编译
    /// </summary>
    [Fact]
    public void TestReference_OutputCompiles() {
        var compilation = TestCompilation.Create(
            @"namespace Lib { public class @class<T> { public class @int<U> { } } }",
            @"namespace Sample { public class Customer { } }" );
        var inner = ConstructNested( compilation, "Lib.class`1", "int", SpecialType.System_Int32, SpecialType.System_String );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { inner } );
        var reference = scope.Reference( inner );
        Assert.Equal( "@class<int>.@int<string>", reference );
        var body = "internal static class Probe { public static " + reference + " Value; }";
        var text = GeneratedSource.Wrap( body, scope.Imports, "Probe.Generated" );
        var generated = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText( text, new CSharpParseOptions( LanguageVersion.CSharp10 ), cancellationToken: TestContext.Current.CancellationToken ) );
        Assert.DoesNotContain( generated.GetDiagnostics( TestContext.Current.CancellationToken ),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error );
    }

    /// <summary>
    /// 编译单元为 null 抛出异常
    /// </summary>
    [Fact]
    public void TestConstructor_ThrowsOnNullCompilation() {
        Assert.Throws<ArgumentNullException>( () =>
            new ImportScope( (Compilation)null, new[] { "System" }, new ITypeSymbol[0] ) );
    }

    /// <summary>
    /// 框架 using 清单为 null 抛出异常
    /// </summary>
    [Fact]
    public void TestConstructor_ThrowsOnNullFrameworkImports() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        Assert.Throws<ArgumentNullException>( () =>
            new ImportScope( compilation, null, new ITypeSymbol[0] ) );
    }

    /// <summary>
    /// 有同名类型的业务命名空间自动只保留优先者
    /// </summary>
    [Fact]
    public void TestImports_AutoExcludesConflictingNamespace() {
        var compilation = TestCompilation.Create( ConflictSource );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( "Lib.A", scope.Imports );
        Assert.DoesNotContain( "Lib.B", scope.Imports );
    }

    /// <summary>
    /// 框架清单由调用方负责,内部有重名类型也全部保留且不生成别名
    /// </summary>
    [Fact]
    public void TestImports_FrameworkImports_ArePreserved() {
        var compilation = TestCompilation.Create( @"namespace Lib.Framework { public class Object { } }" );
        var type = compilation.GetTypeByMetadataName( "Lib.Framework.Object" );
        var scope = new ImportScope( compilation, new[] { "System", "Lib.Framework" }, new ITypeSymbol[] { type } );
        Assert.Contains( "System", scope.Imports );
        Assert.Contains( "Lib.Framework", scope.Imports );
        Assert.DoesNotContain( scope.Imports, item => item.Contains( " = " ) );
    }

    /// <summary>
    /// 被排除命名空间的已用类型生成别名声明
    /// </summary>
    [Fact]
    public void TestAliases_GeneratedForExcludedType() {
        var compilation = TestCompilation.Create( ConflictSource );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( scope.Imports, item => item == "LibBWidget = global::Lib.B.Widget" );
    }

    /// <summary>
    /// 被排除侧引用使用别名短名,保留侧使用原始短名
    /// </summary>
    [Fact]
    public void TestReference_UsesAliasAndShortName() {
        var compilation = TestCompilation.Create( ConflictSource );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Equal( "Widget", scope.Reference( a ) );
        Assert.Equal( "LibBWidget", scope.Reference( b ) );
    }

    /// <summary>
    /// 别名名与已导入命名空间的类型重名时追加序号
    /// </summary>
    [Fact]
    public void TestAlias_NamingCollision_AppendsIndex() {
        const string source = @"
namespace Lib.A { public class Widget { } public class LibBWidget { } }
namespace Lib.B { public class Widget { } }";
        var compilation = TestCompilation.Create( source );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Contains( scope.Imports, item => item == "LibBWidget_2 = global::Lib.B.Widget" );
    }

    /// <summary>
    /// 泛型与数组类型不生成别名,保留全限定名
    /// </summary>
    [Fact]
    public void TestAlias_ComplexType_FallsBackToQualified() {
        var compilation = TestCompilation.Create( @"
namespace Lib.A { public class Widget { } }
namespace Lib.B { public class Widget { } public class Box<T> { } }" );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" },
            new[] { "Lib.A.Widget", "Lib.B.Widget", "Lib.B.Box<Lib.B.Widget>" } );
        Assert.DoesNotContain( scope.Imports, item => item.Contains( "Box" ) );
        Assert.StartsWith( "global::", scope.Reference( "Lib.B.Box<Lib.B.Widget>" ) );
    }

    /// <summary>
    /// 无冲突时不生成别名
    /// </summary>
    [Fact]
    public void TestImports_NoConflict_NoAlias() {
        var compilation = TestCompilation.Create( @"namespace Lib.A { public class HelperA { } }" );
        var scope = new ImportScope( compilation, new[] { "System" }, new[] { "Lib.A.HelperA" } );
        Assert.Contains( "Lib.A", scope.Imports );
        Assert.DoesNotContain( scope.Imports, item => item.Contains( " = " ) );
    }

    /// <summary>
    /// 导入清单与别名顺序跨构造确定
    /// </summary>
    [Fact]
    public void TestImports_Deterministic() {
        var compilation = TestCompilation.Create( ConflictSource );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var left = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { b, a } );
        var right = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.Equal( left.Imports, right.Imports );
    }

    /// <summary>
    /// 冲突命名空间的别名声明与短名引用可直接编译
    /// </summary>
    [Fact]
    public void TestReference_ConflictingNamespaces_OutputCompiles() {
        var compilation = TestCompilation.Create( ConflictSource );
        var a = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var b = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        var body = "internal static class Probe { public static " + scope.Reference( a ) + " A; public static "
            + scope.Reference( b ) + " B; }";
        var text = GeneratedSource.Wrap( body, scope.Imports, "Probe.Generated" );
        var generated = compilation.AddSyntaxTrees( CSharpSyntaxTree.ParseText( text,
            new CSharpParseOptions( LanguageVersion.CSharp10 ), cancellationToken: TestContext.Current.CancellationToken ) );
        Assert.DoesNotContain( generated.GetDiagnostics( TestContext.Current.CancellationToken ),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error );
    }

    #region 文件命名空间遮蔽

    /// <summary>
    /// 文件命名空间遮蔽桩源码,CallSiteAop命名空间被导入,Sample命名空间因同名类型被排除出导入清单
    /// </summary>
    private const string ShadowSource = @"
namespace CallSiteAop { public class MethodInterceptionContext { } }
namespace Sample { public class Widget { } public class MethodInterceptionContext { } }";

    /// <summary>
    /// 测试文件命名空间遮蔽 - 产物文件所在命名空间声明同名类型时,已导入短名须回退全限定
    /// </summary>
    [Fact]
    public void TestForFile_ShadowedInFileNamespace_Qualifies() {
        var compilation = TestCompilation.Create( ShadowSource );
        var context = compilation.GetTypeByMetadataName( "CallSiteAop.MethodInterceptionContext" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { context } ).ForFile( "Sample" );
        Assert.Equal( "global::CallSiteAop.MethodInterceptionContext", scope.Reference( "CallSiteAop.MethodInterceptionContext" ) );
    }

    /// <summary>
    /// 测试未指定文件命名空间时保持既有裸名行为
    /// </summary>
    [Fact]
    public void TestNoFileContext_KeepsLegacyBareName() {
        var compilation = TestCompilation.Create( ShadowSource );
        var context = compilation.GetTypeByMetadataName( "CallSiteAop.MethodInterceptionContext" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { context } );
        Assert.Equal( "MethodInterceptionContext", scope.Reference( "CallSiteAop.MethodInterceptionContext" ) );
    }

    /// <summary>
    /// 测试目标类型就在文件命名空间时保持短名 - 外层命名空间命中的即目标类型,不构成遮蔽
    /// </summary>
    [Fact]
    public void TestForFile_OwnerInFileNamespace_StaysShort() {
        var compilation = TestCompilation.Create( @"namespace Sample { public class Widget { } }" );
        var widget = compilation.GetTypeByMetadataName( "Sample.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { widget } ).ForFile( "Sample" );
        Assert.Equal( "Widget", scope.Reference( "Sample.Widget" ) );
    }

    /// <summary>
    /// 测试全局命名空间产物文件被全局同名类型遮蔽 - 文件命名空间为空串时按全局链检查
    /// </summary>
    [Fact]
    public void TestForFile_GlobalFileNamespace_ShadowedByGlobalDecoy() {
        var compilation = TestCompilation.Create( @"
public class MethodInterceptionContext { }
namespace CallSiteAop { public class MethodInterceptionContext { } }" );
        var context = compilation.GetTypeByMetadataName( "CallSiteAop.MethodInterceptionContext" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { context } ).ForFile( "" );
        Assert.Equal( "global::CallSiteAop.MethodInterceptionContext", scope.Reference( "CallSiteAop.MethodInterceptionContext" ) );
    }

    /// <summary>
    /// 测试泛型占位类型按简单名遮蔽 - 成员名 Foo`1 与短名 Foo 视为同名
    /// </summary>
    [Fact]
    public void TestForFile_GenericArityDecoy_Shadowed() {
        var compilation = TestCompilation.Create( @"
namespace CallSiteAop { public class Result<T> { } }
namespace Sample { public class Result<T> { } public class Widget { } }" );
        var result = compilation.GetTypeByMetadataName( "CallSiteAop.Result`1" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { result } ).ForFile( "Sample" );
        Assert.Equal( "global::CallSiteAop.Result<int>", scope.Reference( "CallSiteAop.Result<int>" ) );
    }

    /// <summary>
    /// 测试类型符号引用同样受文件命名空间遮蔽检查
    /// </summary>
    [Fact]
    public void TestForFile_SymbolReference_ShadowedQualifies() {
        var compilation = TestCompilation.Create( ShadowSource );
        var context = compilation.GetTypeByMetadataName( "CallSiteAop.MethodInterceptionContext" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { context } ).ForFile( "Sample" );
        var symbol = compilation.GetTypeByMetadataName( "CallSiteAop.MethodInterceptionContext" );
        Assert.Equal( "global::CallSiteAop.MethodInterceptionContext", scope.Reference( symbol ) );
    }

    /// <summary>
    /// 测试文件命名空间内同名命名空间遮蔽时按全限定名输出且产物可编译
    /// </summary>
    [Fact]
    public void TestForFile_NamespaceDecoy_QualifiesAndCompiles() {
        var compilation = TestCompilation.Create(
            "namespace Lib { public class Customer { public static int Marker; } }",
            "namespace Sample { namespace Customer { } public class Widget { } }" );
        var target = compilation.GetTypeByMetadataName( "Lib.Customer" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { target } ).ForFile( "Sample" );
        var reference = scope.Reference( "Lib.Customer" );
        Assert.Equal( "global::Lib.Customer", reference );
        Assert.Equal( "global::Lib.Customer", scope.Reference( target ) );
        var body = "internal static class Probe { public static int Value = " + reference + ".Marker; }";
        var text = GeneratedSource.Wrap( body, scope.Imports, "Sample" );
        var generated = compilation.AddSyntaxTrees( CSharpSyntaxTree.ParseText( text,
            new CSharpParseOptions( LanguageVersion.CSharp10 ), cancellationToken: TestContext.Current.CancellationToken ) );
        Assert.DoesNotContain( generated.GetDiagnostics( TestContext.Current.CancellationToken ),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error );
    }

    /// <summary>
    /// 测试关键字转义类型被同名类型遮蔽时按全限定名输出且产物可编译
    /// </summary>
    [Fact]
    public void TestForFile_KeywordDecoy_QualifiesAndCompiles() {
        var compilation = TestCompilation.Create(
            "namespace Lib { public class @class { public static int Marker; } }",
            "namespace Sample { public class @class { } }" );
        var target = compilation.GetTypeByMetadataName( "Lib.class" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { target } ).ForFile( "Sample" );
        var reference = scope.Reference( target );
        Assert.Equal( "global::Lib.@class", reference );
        var body = "internal static class Probe { public static int Value = " + reference + ".Marker; }";
        var text = GeneratedSource.Wrap( body, scope.Imports, "Sample" );
        var generated = compilation.AddSyntaxTrees( CSharpSyntaxTree.ParseText( text,
            new CSharpParseOptions( LanguageVersion.CSharp10 ), cancellationToken: TestContext.Current.CancellationToken ) );
        Assert.DoesNotContain( generated.GetDiagnostics( TestContext.Current.CancellationToken ),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error );
    }

    /// <summary>
    /// 测试别名与文件命名空间内类型同名时不声明冲突别名且产物可编译
    /// </summary>
    [Fact]
    public void TestForFile_AliasNameConflict_Compiles() {
        var compilation = TestCompilation.Create(
            "namespace Lib.A { public class Widget { public static int Marker; } }",
            "namespace Lib.B { public class Widget { public static int Marker; } }",
            "namespace Sample { public class LibBWidget { } }" );
        var first = compilation.GetTypeByMetadataName( "Lib.A.Widget" );
        var second = compilation.GetTypeByMetadataName( "Lib.B.Widget" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { first, second } ).ForFile( "Sample" );
        Assert.DoesNotContain( scope.Imports, item => item == "LibBWidget = global::Lib.B.Widget" );
        var reference = scope.Reference( second );
        Assert.StartsWith( "global::", reference );
        var body = "internal static class Probe { public static int Value = " + reference + ".Marker; }";
        var text = GeneratedSource.Wrap( body, scope.Imports, "Sample" );
        var generated = compilation.AddSyntaxTrees( CSharpSyntaxTree.ParseText( text,
            new CSharpParseOptions( LanguageVersion.CSharp10 ), cancellationToken: TestContext.Current.CancellationToken ) );
        Assert.DoesNotContain( generated.GetDiagnostics( TestContext.Current.CancellationToken ),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error );
    }

    #endregion

}
