namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 编译单元索引测试
/// </summary>
public class CompilationIndexTest {
    /// <summary>
    /// 同源码生成相等索引
    /// </summary>
    [Fact]
    public void TestEquality_SameSource() {
        var source = @"
            namespace Sample.Domain;

            public class Customer {
                public string Name { get; set; }
            }

            public enum Level {
                Low = 1
            }";
        var left = CompilationIndex.From( TestCompilation.Create( source ), TestContext.Current.CancellationToken );
        var right = CompilationIndex.From( TestCompilation.Create( source ), TestContext.Current.CancellationToken );
        Assert.Equal( left, right );
        Assert.Equal( left.GetHashCode(), right.GetHashCode() );
    }

    /// <summary>
    /// 新增类型使索引不相等
    /// </summary>
    [Fact]
    public void TestEquality_AddedType() {
        var baseSource = @"
            namespace Sample.Domain;

            public class Customer {
            }";
        var addedSource = @"
            namespace Sample.Domain;

            public class Order {
            }";
        var left = CompilationIndex.From( TestCompilation.Create( baseSource ), TestContext.Current.CancellationToken );
        var right = CompilationIndex.From( TestCompilation.Create( baseSource, addedSource ), TestContext.Current.CancellationToken );
        Assert.NotEqual( left, right );
    }

    /// <summary>
    /// 探测顶层类型、命名空间与命名空间成员
    /// </summary>
    [Fact]
    public void TestProbe_TypeAndNamespace() {
        var index = CompilationIndex.From( TestCompilation.Create( @"
            namespace Sample.Domain {
                public class Customer {
                    public class Inner {
                    }
                }
            }

            namespace Other {
                public enum Level {
                    A = 1
                }
            }" ), TestContext.Current.CancellationToken );
        Assert.True( index.TypeExists( "Sample.Domain.Customer" ) );
        Assert.False( index.TypeExists( "Sample.Domain.NotExist" ) );
        Assert.False( index.TypeExists( "Sample.Domain.Customer.Inner" ) );
        Assert.True( index.ContainsNamespace( "Sample.Domain" ) );
        Assert.False( index.ContainsNamespace( "Sample.Domain.Customer" ) );
        Assert.True( index.HasNamespaceMember( "Sample.Domain", "Customer" ) );
        Assert.False( index.HasNamespaceMember( "Sample.Domain", "Inner" ) );
        Assert.True( index.HasNamespaceMember( "Other", "Level" ) );
        Assert.Equal( "SharedTestStub", index.AssemblyName );
    }

    /// <summary>
    /// 取消令牌触发取消
    /// </summary>
    [Fact]
    public void TestFrom_ThrowsWhenCancelled() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        Assert.Throws<OperationCanceledException>( () =>
            CompilationIndex.From( compilation, new CancellationToken( true ) ) );
    }

    /// <summary>
    /// 同一编译单元复用缓存索引
    /// </summary>
    [Fact]
    public void TestFrom_ReusesIndexPerCompilation() {
        var compilation = TestCompilation.Create( "namespace Sample { public class Customer { } }" );
        var first = CompilationIndex.From( compilation, TestContext.Current.CancellationToken );
        var second = CompilationIndex.From( compilation, TestContext.Current.CancellationToken );
        Assert.Same( first, second );
    }

    /// <summary>
    /// 索引覆盖引用程序集、嵌套命名空间与泛型元数据名
    /// </summary>
    [Fact]
    public void TestProbe_ReferenceTypesAndNestedNamespace() {
        var index = CompilationIndex.From( TestCompilation.Create( @"
            namespace Outer.Inner { public class Deep { } }
            namespace Sample { public class Customer<T> { } }" ), TestContext.Current.CancellationToken );
        Assert.True( index.TypeExists( "System.Object" ) );
        Assert.True( index.ContainsNamespace( "Outer.Inner" ) );
        Assert.True( index.TypeExists( "Outer.Inner.Deep" ) );
        Assert.True( index.TypeExists( "Sample.Customer`1" ) );
        Assert.False( index.TypeExists( "Sample.Customer" ) );
        Assert.True( index.HasNamespaceMember( "Outer.Inner", "Deep" ) );
    }

    /// <summary>
    /// null 探测参数返回 false
    /// </summary>
    [Fact]
    public void TestProbe_NullArguments() {
        var index = CompilationIndex.From( TestCompilation.Create( "namespace Sample { public class Customer { } }" ),
            TestContext.Current.CancellationToken );
        Assert.False( index.TypeExists( null ) );
        Assert.False( index.ContainsNamespace( null ) );
        Assert.False( index.HasNamespaceMember( null, "Customer" ) );
        Assert.False( index.HasNamespaceMember( "Sample", null ) );
    }

    /// <summary>
    /// null、非索引对象与不同程序集名不相等
    /// </summary>
    [Fact]
    public void TestEquality_NullAndAssemblyName() {
        const string source = "namespace Sample { public class Customer { } }";
        var left = CompilationIndex.From( TestCompilation.Create( source ), TestContext.Current.CancellationToken );
        var right = CompilationIndex.From( TestCompilation.CreateWithAssemblyName( "OtherStub", source ),
            TestContext.Current.CancellationToken );
        Assert.False( left.Equals( (CompilationIndex)null ) );
        Assert.False( left.Equals( (object)null ) );
        Assert.False( left.Equals( new object() ) );
        Assert.NotEqual( left, right );
    }
}
