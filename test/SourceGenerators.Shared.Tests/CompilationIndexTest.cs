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
}
