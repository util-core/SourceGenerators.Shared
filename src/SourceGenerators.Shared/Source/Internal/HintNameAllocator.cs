namespace SourceGenerators.Shared.Internal;

/// <summary>
/// 提示文件名分配器,净化非法字符并对撞名追加序号
/// </summary>
internal sealed class HintNameAllocator {
    /// <summary>
    /// 生成文件扩展名
    /// </summary>
    private const string FileExtension = ".g.cs";
    /// <summary>
    /// 已占用的生成文件名
    /// </summary>
    private readonly HashSet<string> _used;

    /// <summary>
    /// 初始化提示文件名分配器
    /// </summary>
    internal HintNameAllocator( IEnumerable<string> reservedNames = null ) {
        _used = new HashSet<string>( StringComparer.Ordinal );
        if( reservedNames == null )
            return;
        foreach( var name in reservedNames ) {
            if( string.IsNullOrEmpty( name ) )
                continue;
            _used.Add( Sanitize( name ) + FileExtension );
        }
    }

    /// <summary>
    /// 分配唯一生成文件名,短名后接 .g.cs,撞名时在扩展名前追加 _2、_3 序号
    /// </summary>
    internal string Allocate( string shortName ) {
        if( shortName == null )
            throw new ArgumentNullException( nameof( shortName ) );
        if( shortName.Length == 0 )
            throw new ArgumentException( "文件名短名不能为空", nameof( shortName ) );
        var name = Sanitize( shortName );
        var candidate = name + FileExtension;
        for( var index = 2; _used.Contains( candidate ); index++ )
            candidate = name + "_" + index.ToString( CultureInfo.InvariantCulture ) + FileExtension;
        _used.Add( candidate );
        return candidate;
    }

    /// <summary>
    /// 非法字符替换为下划线,仅保留字母、数字与下划线
    /// </summary>
    private static string Sanitize( string value ) {
        var builder = new StringBuilder( value.Length );
        foreach( var character in value ) {
            if( char.IsLetterOrDigit( character ) || character == '_' )
                builder.Append( character );
            else
                builder.Append( '_' );
        }
        return builder.ToString();
    }
}
