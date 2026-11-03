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
}
