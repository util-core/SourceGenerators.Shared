namespace SourceGenerators.Shared.Internal;

/// <summary>
/// 产物类型引用作用域,构建using清单并规避短名歧义
/// </summary>
internal sealed class ImportScope {
    /// <summary>
    /// 短名有歧义的类型元数据名
    /// </summary>
    private readonly HashSet<string> _ambiguousNames;
    /// <summary>
    /// 互斥命名空间,先进入清单者优先
    /// </summary>
    private readonly HashSet<string> _conflictingNamespaces;
    /// <summary>
    /// 产物using清单
    /// </summary>
    private readonly string[] _imports;
    /// <summary>
    /// 当前编译单元,索引构造的实例为null
    /// </summary>
    private readonly Compilation _compilation;
    /// <summary>
    /// 名称探测索引
    /// </summary>
    private readonly CompilationIndex _index;

    /// <summary>
    /// 按类型符号建域
    /// </summary>
    internal ImportScope( Compilation compilation, string[] frameworkImports, IEnumerable<ITypeSymbol> types,
            IEnumerable<string> conflictingNamespaces = null )
        : this( CompilationIndex.From( compilation ), frameworkImports, types, conflictingNamespaces ) {
        _compilation = compilation;
    }

    /// <summary>
    /// 按类型符号建域,名称探测走索引
    /// </summary>
    internal ImportScope( CompilationIndex index, string[] frameworkImports, IEnumerable<ITypeSymbol> types,
            IEnumerable<string> conflictingNamespaces = null ) {
        _index = index;
        _ambiguousNames = new HashSet<string>( StringComparer.Ordinal );
        _conflictingNamespaces = CreateConflictSet( conflictingNamespaces );
        var shortNames = new Dictionary<string, SortedSet<string>>( StringComparer.Ordinal );
        foreach( var type in Flatten( types ) ) {
            if( type == null )
                continue;
            var namespaceName = type.ContainingNamespace;
            if( namespaceName == null || namespaceName.IsGlobalNamespace )
                continue;
            var owner = namespaceName.ToDisplayString();
            if( frameworkImports.Contains( owner, StringComparer.Ordinal ) )
                continue;
            var metadataName = ResolvingName( type );
            if( shortNames.TryGetValue( metadataName, out var owners ) == false ) {
                owners = new SortedSet<string>( StringComparer.Ordinal );
                shortNames.Add( metadataName, owners );
            }
            owners.Add( owner );
        }
        _imports = Build( index, frameworkImports, shortNames, _ambiguousNames, _conflictingNamespaces );
    }

    /// <summary>
    /// 按全限定名建域
    /// </summary>
    internal ImportScope( Compilation compilation, string[] frameworkImports, IEnumerable<string> fullNames,
            IEnumerable<string> conflictingNamespaces = null )
        : this( CompilationIndex.From( compilation ), frameworkImports, fullNames, conflictingNamespaces ) {
        _compilation = compilation;
    }

    /// <summary>
    /// 按全限定名建域,名称探测走索引
    /// </summary>
    internal ImportScope( CompilationIndex index, string[] frameworkImports, IEnumerable<string> fullNames,
            IEnumerable<string> conflictingNamespaces = null ) {
        _index = index;
        _ambiguousNames = new HashSet<string>( StringComparer.Ordinal );
        _conflictingNamespaces = CreateConflictSet( conflictingNamespaces );
        var shortNames = new Dictionary<string, SortedSet<string>>( StringComparer.Ordinal );
        foreach( var segment in Flatten( fullNames ) ) {
            var ( owner, metadataName ) = Split( segment );
            if( owner == null )
                continue;
            if( IsNamespace( owner ) == false ) {
                var nested = SplitNested( segment );
                if( nested == null )
                    continue;
                owner = nested.Value.Owner;
                metadataName = nested.Value.Id;
            }
            if( frameworkImports.Contains( owner, StringComparer.Ordinal ) )
                continue;
            if( shortNames.TryGetValue( metadataName, out var owners ) == false ) {
                owners = new SortedSet<string>( StringComparer.Ordinal );
                shortNames.Add( metadataName, owners );
            }
            owners.Add( owner );
        }
        _imports = Build( index, frameworkImports, shortNames, _ambiguousNames, _conflictingNamespaces );
    }

    /// <summary>
    /// 当前编译单元,索引构造的实例为null
    /// </summary>
    internal Compilation Compilation => _compilation;

    /// <summary>
    /// 产物using清单
    /// </summary>
    internal string[] Imports => _imports;

    /// <summary>
    /// 构建互斥命名空间集合
    /// </summary>
    private static HashSet<string> CreateConflictSet( IEnumerable<string> conflictingNamespaces ) {
        if( conflictingNamespaces == null )
            return new HashSet<string>( StringComparer.Ordinal );
        return new HashSet<string>( conflictingNamespaces, StringComparer.Ordinal );
    }

    /// <summary>
    /// 展开数组、泛型实参与类型参数
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> Flatten( IEnumerable<ITypeSymbol> types ) {
        foreach( var type in types ) {
            if( type == null )
                continue;
            if( type is IArrayTypeSymbol array ) {
                foreach( var item in Flatten( new[] { array.ElementType } ) )
                    yield return item;
                continue;
            }
            if( type is INamedTypeSymbol named ) {
                yield return named;
                foreach( var argument in named.TypeArguments ) {
                    foreach( var item in Flatten( new[] { argument } ) )
                        yield return item;
                }
                continue;
            }
            if( type is not ITypeParameterSymbol )
                yield return null;
        }
    }

    /// <summary>
    /// 短名解析标识,嵌套类型取最外层类型名
    /// </summary>
    private static string ResolvingName( INamedTypeSymbol type ) {
        var current = type;
        while( current.ContainingType != null )
            current = current.ContainingType;
        return current.MetadataName;
    }

    /// <summary>
    /// 加入框架与业务命名空间,歧义短名保留全限定名
    /// </summary>
    private static string[] Build( CompilationIndex index, string[] frameworkImports, Dictionary<string, SortedSet<string>> shortNames,
            HashSet<string> ambiguousNames, HashSet<string> conflictingNamespaces ) {
        var list = new List<string>( frameworkImports.Length );
        foreach( var name in frameworkImports )
            Add( list, name, conflictingNamespaces );
        foreach( var pair in shortNames ) {
            if( pair.Value.Count > 1 || ExistsInFramework( index, frameworkImports, pair.Key ) ) {
                ambiguousNames.Add( pair.Key );
                continue;
            }
            foreach( var owner in pair.Value )
                Add( list, owner, conflictingNamespaces );
        }
        return list.ToArray();
    }

    /// <summary>
    /// 追加命名空间,重复与冲突项忽略
    /// </summary>
    private static void Add( List<string> list, string namespaceName, HashSet<string> conflictingNamespaces ) {
        if( namespaceName == null || list.Contains( namespaceName, StringComparer.Ordinal ) )
            return;
        if( Conflicts( list, namespaceName, conflictingNamespaces ) )
            return;
        list.Add( namespaceName );
    }

    /// <summary>
    /// 是否与清单内已有命名空间互斥
    /// </summary>
    private static bool Conflicts( List<string> list, string namespaceName, HashSet<string> conflictingNamespaces ) {
        if( conflictingNamespaces == null || conflictingNamespaces.Contains( namespaceName ) == false )
            return false;
        foreach( var existing in list ) {
            if( conflictingNamespaces.Contains( existing ) )
                return true;
        }
        return false;
    }

    /// <summary>
    /// 框架命名空间下是否已存在同名类型
    /// </summary>
    private static bool ExistsInFramework( CompilationIndex index, string[] frameworkImports, string metadataName ) {
        foreach( var imported in frameworkImports )
            if( index.TypeExists( $"{imported}.{metadataName}" ) )
                return true;
        return false;
    }

    /// <summary>
    /// 展开全限定名中的泛型实参
    /// </summary>
    private static IEnumerable<string> Flatten( IEnumerable<string> fullNames ) {
        foreach( var fullName in fullNames ) {
            var text = StripGlobal( fullName );
            var generic = text.IndexOf( '<' );
            if( generic < 0 ) {
                yield return text;
                continue;
            }
            yield return text.Substring( 0, generic );
            var body = text.Substring( generic + 1, text.LastIndexOf( '>' ) - generic - 1 );
            foreach( var argument in SplitTopLevel( body ) )
                foreach( var segment in Flatten( new[] { argument } ) )
                    yield return segment;
        }
    }

    /// <summary>
    /// 去掉global::前缀
    /// </summary>
    private static string StripGlobal( string text ) {
        return text.StartsWith( "global::", StringComparison.Ordinal ) ? text.Substring( 8 ) : text;
    }

    /// <summary>
    /// 按顶层逗号拆分泛型实参
    /// </summary>
    private static IEnumerable<string> SplitTopLevel( string body ) {
        var depth = 0;
        var start = 0;
        for( var i = 0; i < body.Length; i++ ) {
            var current = body[i];
            if( current == '<' )
                depth++;
            else if( current == '>' )
                depth--;
            else if( current == ',' && depth == 0 ) {
                yield return body.Substring( start, i - start ).Trim();
                start = i + 1;
            }
        }
        var last = body.Substring( start ).Trim();
        if( last.Length > 0 )
            yield return last;
    }

    /// <summary>
    /// 拆出owner与末段类型名
    /// </summary>
    private static (string Owner, string MetadataName) Split( string fullName ) {
        var text = StripGlobal( fullName );
        var generic = text.IndexOf( '<' );
        var head = generic < 0 ? text : text.Substring( 0, generic );
        var index = head.LastIndexOf( '.' );
        if( index < 0 )
            return (null, null);
        return (head.Substring( 0, index ), head.Substring( index + 1 ));
    }

    /// <summary>
    /// 全名段是否为命名空间
    /// </summary>
    private bool IsNamespace( string namespaceName ) {
        if( _index.TypeExists( namespaceName ) )
            return false;
        return _index.ContainsNamespace( namespaceName );
    }

    /// <summary>
    /// 回溯嵌套类型所属命名空间
    /// </summary>
    private (string Owner, string Id, string Chain)? SplitNested( string fullName ) {
        var segments = StripGlobal( fullName ).Split( '.' );
        for( var i = 0; i < segments.Length - 1; i++ ) {
            if( _index.TypeExists( string.Join( ".", segments, 0, i + 1 ) ) == false )
                continue;
            if( i == 0 )
                return null;
            var owner = string.Join( ".", segments, 0, i );
            if( _index.ContainsNamespace( owner ) == false )
                return null;
            return (owner, segments[i], string.Join( ".", segments, i, segments.Length - i ));
        }
        return null;
    }

    /// <summary>
    /// 类型引用名,已导入且无歧义时用短名
    /// </summary>
    internal string Reference( ITypeSymbol type ) {
        if( type is IArrayTypeSymbol array )
            return Reference( array.ElementType ) + "[]";
        if( type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable )
            return Reference( nullable.TypeArguments[0] ) + "?";
        if( type is INamedTypeSymbol named && named.IsGenericType ) {
            var definition = named.ConstructedFrom;
            var arguments = named.TypeArguments.Select( Reference );
            return ResolveDefinition( definition, arguments );
        }
        var full = GeneratedSource.Full( type );
        var metadataName = type.MetadataName;
        if( type is not INamedTypeSymbol namedType || namedType.ContainingType == null ) {
            var ( owner, name ) = Split( full );
            if( owner == null )
                return full;
            if( Imported( owner ) == false || IsAmbiguous( owner, metadataName ) )
                return full;
            return name;
        }
        for( var current = namedType.ContainingType; current != null; current = current.ContainingType ) {
            if( current.IsGenericType )
                return full;
        }
        var namespaceName = namedType.ContainingNamespace;
        if( namespaceName == null || namespaceName.IsGlobalNamespace )
            return full;
        var ns = namespaceName.ToDisplayString();
        var resolvingName = ResolvingName( namedType );
        if( Imported( ns ) == false || IsAmbiguous( ns, resolvingName ) )
            return full;
        return BuildNestedChain( namedType );
    }

    /// <summary>
    /// 泛型定义引用名
    /// </summary>
    private string ResolveDefinition( INamedTypeSymbol definition, IEnumerable<string> arguments ) {
        var joined = string.Join( ", ", arguments );
        var full = GeneratedSource.FullDefinition( definition );
        var ( owner, _ ) = Split( full );
        var metadataName = definition.MetadataName;
        var shortName = Strip( metadataName );
        if( owner == null || Imported( owner ) == false || IsAmbiguous( owner, metadataName ) )
            return full + "<" + joined + ">";
        return shortName + "<" + joined + ">";
    }

    /// <summary>
    /// 去掉元数据名的元数后缀
    /// </summary>
    private static string Strip( string metadataName ) {
        var tick = metadataName.IndexOf( '`' );
        return tick < 0 ? metadataName : metadataName.Substring( 0, tick );
    }

    /// <summary>
    /// 命名空间是否已导入,System子命名空间一并视为导入
    /// </summary>
    private bool Imported( string namespaceName ) {
        if( _imports.Contains( namespaceName, StringComparer.Ordinal ) )
            return true;
        foreach( var imported in _imports ) {
            if( imported != GeneratedSource.SystemNamespace )
                continue;
            if( namespaceName.StartsWith( imported + ".", StringComparison.Ordinal ) )
                return true;
        }
        return false;
    }

    /// <summary>
    /// 短名是否被其它导入命名空间占用
    /// </summary>
    private bool IsAmbiguous( string owner, string metadataName ) {
        if( _ambiguousNames.Contains( metadataName ) )
            return true;
        foreach( var imported in _imports ) {
            if( imported == owner )
                continue;
            if( ExistsMember( imported, metadataName ) )
                return true;
        }
        return false;
    }

    /// <summary>
    /// 命名空间是否包含指定顶层类型,不要求全名唯一
    /// </summary>
    private bool ExistsMember( string namespaceName, string metadataName ) {
        return _index.HasNamespaceMember( namespaceName, metadataName );
    }

    /// <summary>
    /// 构建嵌套类型链
    /// </summary>
    private static string BuildNestedChain( INamedTypeSymbol type ) {
        var segments = new Stack<string>();
        for( var current = type; current != null; current = current.ContainingType )
            segments.Push( current.Name );
        return string.Join( ".", segments );
    }

    /// <summary>
    /// 全限定名引用名
    /// </summary>
    internal string Reference( string fullName ) {
        var text = StripGlobal( fullName );
        var generic = text.IndexOf( '<' );
        if( generic < 0 )
            return ReferenceCore( text );
        var head = text.Substring( 0, generic );
        var arguments = SplitArguments( text );
        return ReferenceCore( head ) + "<" + arguments + ">";
    }

    /// <summary>
    /// 单个类型段短名化
    /// </summary>
    private string ReferenceCore( string segment ) {
        var ( owner, metadataName ) = Split( segment );
        if( owner == null )
            return segment;
        if( IsNamespace( owner ) == false ) {
            var nested = SplitNested( segment );
            if( nested == null || Imported( nested.Value.Owner ) == false || IsAmbiguous( nested.Value.Owner, nested.Value.Id ) )
                return "global::" + segment;
            return nested.Value.Chain;
        }
        if( Imported( owner ) == false || IsAmbiguous( owner, metadataName ) )
            return "global::" + segment;
        return metadataName;
    }

    /// <summary>
    /// 递归处理泛型实参
    /// </summary>
    private string SplitArguments( string text ) {
        var start = text.IndexOf( '<' ) + 1;
        var body = text.Substring( start, text.LastIndexOf( '>' ) - start );
        return string.Join( ", ", SplitTopLevel( body ).Select( Reference ) );
    }

    /// <summary>
    /// 未参与建域的类型引用名,未导入或有歧义时保留全限定名
    /// </summary>
    internal string ReferenceExternal( string fullName ) {
        var ( owner, metadataName ) = Split( fullName );
        if( owner == null || metadataName == null )
            return "global::" + StripGlobal( fullName );
        if( Imported( owner ) == false || IsAmbiguous( owner, metadataName ) )
            return "global::" + StripGlobal( fullName );
        return metadataName;
    }
}
