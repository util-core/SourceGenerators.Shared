namespace SourceGenerators.Shared.Tests;

/// <summary>
/// 测试用桩编译单元
/// </summary>
internal static class TestCompilation {
    /// <summary>
    /// 创建桩编译单元
    /// </summary>
    internal static CSharpCompilation Create( params string[] sources ) {
        var trees = sources.Select( source => CSharpSyntaxTree.ParseText( source, new CSharpParseOptions( LanguageVersion.CSharp10 ) ) ).ToArray();
        var references = new[] {
            MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
            MetadataReference.CreateFromFile( typeof( Enumerable ).Assembly.Location )
        };
        return CSharpCompilation.Create( "SharedTestStub", trees, references, new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
    }
}
