namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 注入源码在消费方的编译兼容性测试
/// </summary>
public class InjectedSourceTest {

    #region 测试初始化

    /// <summary>
    /// 按消费方启用可空性的配置重新编译注入源码
    /// </summary>
    private static CSharpCompilation CreateConsumerCompilation() {
        return CSharpCompilation.Create( "ConsumerProbe", ReadInjectedTrees(), CreateReferences(),
            new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable ) );
    }

    /// <summary>
    /// 读取嵌入的源码文本
    /// </summary>
    private static string ReadResource( Assembly assembly, string name ) {
        using var stream = assembly.GetManifestResourceStream( name );
        using var reader = new StreamReader( stream );
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 构建消费方编译所需的元数据引用
    /// </summary>
    private static MetadataReference[] CreateReferences() {
        var paths = new HashSet<string>( StringComparer.Ordinal );
        var trusted = (string)AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" );
        if( trusted != null )
            foreach( var path in trusted.Split( Path.PathSeparator ) )
                if( path.Length > 0 )
                    paths.Add( path );
        paths.Add( typeof( Compilation ).Assembly.Location );
        return paths.Select( path => MetadataReference.CreateFromFile( path ) ).ToArray();
    }

    /// <summary>
    /// 取嵌入源码的语法树
    /// </summary>
    private static SyntaxTree[] ReadInjectedTrees() {
        var assembly = typeof( InjectedSourceTest ).Assembly;
        return assembly.GetManifestResourceNames()
            .Where( name => name.StartsWith( "Injected.", StringComparison.Ordinal ) )
            .Select( name => CSharpSyntaxTree.ParseText( ReadResource( assembly, name ),
                new CSharpParseOptions( LanguageVersion.CSharp10 ), cancellationToken: TestContext.Current.CancellationToken ) )
            .ToArray();
    }

    #endregion

    #region 可空性

    /// <summary>
    /// 测试注入源码在启用可空性的消费方不产生可空警告与错误
    /// </summary>
    [Fact]
    public void TestInjectedSources_NoNullableWarnings() {
        var compilation = CreateConsumerCompilation();
        var diagnostics = compilation.GetDiagnostics( TestContext.Current.CancellationToken );
        var nullableDiagnostics = diagnostics
            .Where( item => item.Id.StartsWith( "CS86", StringComparison.Ordinal ) ).ToArray();
        Assert.True( nullableDiagnostics.Length == 0,
            "可空性诊断: " + string.Join( "; ", nullableDiagnostics.Select( item => item.ToString() ) ) );
        Assert.DoesNotContain( diagnostics, item => item.Severity == DiagnosticSeverity.Error );
    }

    #endregion

    #region 全局 using 隔离

    /// <summary>
    /// 测试注入源码不向消费方编译单元声明全局 using
    /// </summary>
    [Fact]
    public void TestInjectedSources_NoGlobalUsingDirectives() {
        var trees = ReadInjectedTrees();
        Assert.NotEmpty( trees );
        foreach( var tree in trees )
            Assert.DoesNotContain( tree.GetRoot( TestContext.Current.CancellationToken )
                .DescendantNodes().OfType<UsingDirectiveSyntax>(),
                item => item.GlobalKeyword.IsKind( SyntaxKind.GlobalKeyword ) );
    }

    #endregion
}
