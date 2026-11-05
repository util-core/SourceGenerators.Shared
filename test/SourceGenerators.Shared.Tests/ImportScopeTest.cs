namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 类型引用作用域测试
/// </summary>
public class ImportScopeTest {
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
    /// 互斥命名空间只保留先进入者,另一命名空间的类型保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_HonorsConflictingNamespaces() {
        var compilation = TestCompilation.Create( @"namespace Sample.A { public class A { } } namespace Sample.B { public class B { } }" );
        var a = compilation.GetTypeByMetadataName( "Sample.A.A" );
        var b = compilation.GetTypeByMetadataName( "Sample.B.B" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b }, new[] { "Sample.A", "Sample.B" } );
        Assert.Single( scope.Imports, name => name == "Sample.A" || name == "Sample.B" );
        var imported = scope.Imports.Single( name => name == "Sample.A" || name == "Sample.B" );
        var skipped = imported == "Sample.A" ? b : a;
        Assert.StartsWith( "global::", scope.Reference( skipped ) );
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
    /// 两个命名空间的嵌套链首段同名时保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_NestedType_SameOuterName_Qualified() {
        var compilation = TestCompilation.Create(
            @"namespace Lib.Conflict.A { public class Wrapper { public class Item { } } }",
            @"namespace Lib.Conflict.B { public class Wrapper { public class Item { } } }" );
        var a = GetNestedType( compilation, "Lib.Conflict.A.Wrapper", "Item" );
        var b = GetNestedType( compilation, "Lib.Conflict.B.Wrapper", "Item" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.DoesNotContain( "Lib.Conflict.A", scope.Imports );
        Assert.DoesNotContain( "Lib.Conflict.B", scope.Imports );
        Assert.Equal( "global::Lib.Conflict.A.Wrapper.Item", scope.Reference( a ) );
        Assert.Equal( "global::Lib.Conflict.B.Wrapper.Item", scope.Reference( b ) );
    }

    /// <summary>
    /// 嵌套链首段与已导入命名空间的顶层类型同名时保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_NestedType_OuterNameCollidesTopLevel_Qualified() {
        var compilation = TestCompilation.Create(
            @"namespace Lib.Samples { public class OuterEntity { public class InnerEntity { } } }",
            @"namespace Sample.Isolated { public class OuterEntity { } }" );
        var inner = GetNestedType( compilation, "Lib.Samples.OuterEntity", "InnerEntity" );
        var top = compilation.GetTypeByMetadataName( "Sample.Isolated.OuterEntity" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { inner, top } );
        Assert.DoesNotContain( "Lib.Samples", scope.Imports );
        Assert.DoesNotContain( "Sample.Isolated", scope.Imports );
        Assert.Equal( "global::Lib.Samples.OuterEntity.InnerEntity", scope.Reference( inner ) );
        Assert.Equal( "global::Sample.Isolated.OuterEntity", scope.Reference( top ) );
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
    /// 外层类型歧义时仅限定头部,类型实参仍缩短
    /// </summary>
    [Fact]
    public void TestReference_NestedGeneric_AmbiguousOuter_QualifiesHeadOnly() {
        var compilation = TestCompilation.Create(
            @"namespace Lib.A { public class Outer<T> { public class Inner<U> { } } }",
            @"namespace Lib.B { public class Outer<T> { public class Inner<U> { } } }" );
        var a = ConstructNested( compilation, "Lib.A.Outer`1", "Inner", SpecialType.System_Int32, SpecialType.System_String );
        var b = ConstructNested( compilation, "Lib.B.Outer`1", "Inner", SpecialType.System_Int32, SpecialType.System_String );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.DoesNotContain( "Lib.A", scope.Imports );
        Assert.DoesNotContain( "Lib.B", scope.Imports );
        Assert.Equal( "global::Lib.A.Outer<int>.Inner<string>", scope.Reference( a ) );
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
    /// 与框架命名空间同名时保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_FrameworkNameCollision_Qualified() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Object { } }" );
        var type = compilation.GetTypeByMetadataName( "Sample.Object" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { type } );
        Assert.DoesNotContain( "Sample", scope.Imports );
        Assert.Equal( "global::Sample.Object", scope.Reference( type ) );
    }

    /// <summary>
    /// 跨命名空间顶层同名类型保留全限定名
    /// </summary>
    [Fact]
    public void TestReference_TopLevelNameConflict_Qualified() {
        var compilation = TestCompilation.Create(
            @"namespace Sample.A { public class Customer { } }",
            @"namespace Sample.B { public class Customer { } }" );
        var a = compilation.GetTypeByMetadataName( "Sample.A.Customer" );
        var b = compilation.GetTypeByMetadataName( "Sample.B.Customer" );
        var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { a, b } );
        Assert.DoesNotContain( "Sample.A", scope.Imports );
        Assert.DoesNotContain( "Sample.B", scope.Imports );
        Assert.Equal( "global::Sample.A.Customer", scope.Reference( a ) );
        Assert.Equal( "global::Sample.B.Customer", scope.Reference( b ) );
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
}
