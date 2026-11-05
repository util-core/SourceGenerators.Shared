using System.Runtime.CompilerServices;

namespace SourceGenerators.Shared.Internal;

/// <summary>
/// 编译单元名称索引
/// </summary>
internal sealed class CompilationIndex : IEquatable<CompilationIndex> {
    /// <summary>
    /// 按编译单元缓存的索引
    /// </summary>
    private static readonly ConditionalWeakTable<Compilation, CompilationIndex> Cache = new ConditionalWeakTable<Compilation, CompilationIndex>();
    /// <summary>
    /// 顶层类型元数据名出现次数
    /// </summary>
    private readonly Dictionary<string, int> _typeCounts;
    /// <summary>
    /// 命名空间直属类型
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _namespaceMembers;
    /// <summary>
    /// 全部命名空间
    /// </summary>
    private readonly HashSet<string> _namespaces;
    /// <summary>
    /// 程序集名
    /// </summary>
    private readonly string _assemblyName;

    /// <summary>
    /// 初始化编译单元名称索引
    /// </summary>
    private CompilationIndex( Dictionary<string, int> typeCounts, Dictionary<string, HashSet<string>> namespaceMembers,
        HashSet<string> namespaces, string assemblyName ) {
        _typeCounts = typeCounts;
        _namespaceMembers = namespaceMembers;
        _namespaces = namespaces;
        _assemblyName = assemblyName;
    }

    /// <summary>
    /// 当前编译程序集名
    /// </summary>
    internal string AssemblyName => _assemblyName;

    /// <summary>
    /// 构建索引,同一编译单元复用缓存
    /// </summary>
    internal static CompilationIndex From( Compilation compilation, CancellationToken cancellationToken = default ) {
        if( compilation == null )
            throw new ArgumentNullException( nameof( compilation ) );
        cancellationToken.ThrowIfCancellationRequested();
        if( Cache.TryGetValue( compilation, out var cached ) )
            return cached;
        var index = Build( compilation, cancellationToken );
        return Cache.GetValue( compilation, _ => index );
    }

    /// <summary>
    /// 遍历命名空间树构建索引
    /// </summary>
    private static CompilationIndex Build( Compilation compilation, CancellationToken cancellationToken ) {
        var typeCounts = new Dictionary<string, int>( StringComparer.Ordinal );
        var namespaceMembers = new Dictionary<string, HashSet<string>>( StringComparer.Ordinal );
        var namespaces = new HashSet<string>( StringComparer.Ordinal );
        Collect( compilation.GlobalNamespace, string.Empty, typeCounts, namespaceMembers, namespaces, cancellationToken );
        return new CompilationIndex( typeCounts, namespaceMembers, namespaces, compilation.AssemblyName ?? string.Empty );
    }

    /// <summary>
    /// 递归收集命名空间与类型
    /// </summary>
    private static void Collect( INamespaceSymbol namespaceSymbol, string ownerName,
        Dictionary<string, int> typeCounts, Dictionary<string, HashSet<string>> namespaceMembers,
        HashSet<string> namespaces, CancellationToken cancellationToken ) {
        cancellationToken.ThrowIfCancellationRequested();
        var isGlobal = namespaceSymbol.IsGlobalNamespace;
        var fullName = isGlobal ? string.Empty : ownerName.Length == 0 ? namespaceSymbol.Name : ownerName + "." + namespaceSymbol.Name;
        var members = new HashSet<string>( StringComparer.Ordinal );
        foreach( var member in namespaceSymbol.GetMembers() ) {
            if( member is INamespaceSymbol nested ) {
                Collect( nested, fullName, typeCounts, namespaceMembers, namespaces, cancellationToken );
                continue;
            }
            if( member is INamedTypeSymbol type ) {
                members.Add( type.MetadataName );
                var fullMetadataName = fullName.Length == 0 ? type.MetadataName : fullName + "." + type.MetadataName;
                typeCounts.TryGetValue( fullMetadataName, out var count );
                typeCounts[fullMetadataName] = count + 1;
            }
        }
        if( isGlobal == false ) {
            namespaces.Add( fullName );
            namespaceMembers[fullName] = members;
        }
    }

    /// <summary>
    /// 顶层类型是否唯一存在
    /// </summary>
    internal bool TypeExists( string metadataName ) {
        return metadataName != null
            && _typeCounts.TryGetValue( metadataName, out var count ) && count == 1;
    }

    /// <summary>
    /// 命名空间是否存在
    /// </summary>
    internal bool ContainsNamespace( string namespaceName ) {
        return namespaceName != null && _namespaces.Contains( namespaceName );
    }

    /// <summary>
    /// 命名空间是否包含指定顶层类型
    /// </summary>
    internal bool HasNamespaceMember( string namespaceName, string metadataName ) {
        if( namespaceName == null || metadataName == null )
            return false;
        return _namespaceMembers.TryGetValue( namespaceName, out var members ) && members.Contains( metadataName );
    }

    /// <summary>
    /// 取命名空间下的全部顶层类型元数据名
    /// </summary>
    internal IReadOnlyCollection<string> GetNamespaceMembers( string namespaceName ) {
        if( namespaceName != null && _namespaceMembers.TryGetValue( namespaceName, out var members ) )
            return members;
        return Array.Empty<string>();
    }

    /// <summary>
    /// 取多个命名空间之间重复的顶层类型元数据名,按名称排序
    /// </summary>
    internal string[] GetConflictingNames( IEnumerable<string> namespaceNames ) {
        var counts = new Dictionary<string, int>( StringComparer.Ordinal );
        foreach( var namespaceName in namespaceNames ) {
            if( namespaceName == null )
                continue;
            foreach( var member in GetNamespaceMembers( namespaceName ) ) {
                counts.TryGetValue( member, out var count );
                counts[member] = count + 1;
            }
        }
        return counts.Where( pair => pair.Value > 1 ).Select( pair => pair.Key )
            .OrderBy( name => name, StringComparer.Ordinal ).ToArray();
    }

    /// <inheritdoc/>
    public bool Equals( CompilationIndex other ) {
        if( other == null )
            return false;
        if( ReferenceEquals( this, other ) )
            return true;
        if( _assemblyName != other._assemblyName || _typeCounts.Count != other._typeCounts.Count
            || _namespaceMembers.Count != other._namespaceMembers.Count || _namespaces.Count != other._namespaces.Count )
            return false;
        foreach( var pair in _typeCounts ) {
            if( other._typeCounts.TryGetValue( pair.Key, out var count ) == false || count != pair.Value )
                return false;
        }
        foreach( var name in _namespaces ) {
            if( other._namespaces.Contains( name ) == false )
                return false;
        }
        foreach( var pair in _namespaceMembers ) {
            if( other._namespaceMembers.TryGetValue( pair.Key, out var members ) == false || members.SetEquals( pair.Value ) == false )
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals( object obj ) {
        return Equals( obj as CompilationIndex );
    }

    /// <inheritdoc/>
    public override int GetHashCode() {
        unchecked {
            var hash = _assemblyName != null ? _assemblyName.GetHashCode() : 0;
            hash = ( hash * 397 ) ^ _typeCounts.Count;
            hash = ( hash * 397 ) ^ _namespaces.Count;
            return hash;
        }
    }
}
