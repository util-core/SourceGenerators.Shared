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
        : this( CreateIndex( compilation ), frameworkImports, types, conflictingNamespaces ) {
        _compilation = compilation;
    }

    /// <summary>
    /// 按类型符号建域,名称探测走索引
    /// </summary>
    internal ImportScope( CompilationIndex index, string[] frameworkImports, IEnumerable<ITypeSymbol> types,
            IEnumerable<string> conflictingNamespaces = null ) {
        _index = RequireIndex( index );
        RequireImports( frameworkImports );
        if( types == null )
            throw new ArgumentNullException( nameof( types ) );
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
        : this( CreateIndex( compilation ), frameworkImports, fullNames, conflictingNamespaces ) {
        _compilation = compilation;
    }

    /// <summary>
    /// 按全限定名建域,名称探测走索引
    /// </summary>
    internal ImportScope( CompilationIndex index, string[] frameworkImports, IEnumerable<string> fullNames,
            IEnumerable<string> conflictingNamespaces = null ) {
        _index = RequireIndex( index );
        RequireImports( frameworkImports );
        if( fullNames == null )
            throw new ArgumentNullException( nameof( fullNames ) );
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
    /// 创建名称索引,编译单元不能为空
    /// </summary>
    private static CompilationIndex CreateIndex( Compilation compilation ) {
        if( compilation == null )
            throw new ArgumentNullException( nameof( compilation ) );
        return CompilationIndex.From( compilation );
    }

    /// <summary>
    /// 校验名称索引
    /// </summary>
    private static CompilationIndex RequireIndex( CompilationIndex index ) {
        if( index == null )
            throw new ArgumentNullException( nameof( index ) );
        return index;
    }

    /// <summary>
    /// 校验框架using清单
    /// </summary>
    private static void RequireImports( string[] frameworkImports ) {
        if( frameworkImports == null )
            throw new ArgumentNullException( nameof( frameworkImports ) );
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
    /// 展开全限定名中的数组后缀与泛型实参
    /// </summary>
    private static IEnumerable<string> Flatten( IEnumerable<string> fullNames ) {
        foreach( var fullName in fullNames ) {
            if( fullName == null )
                continue;
            var text = TypeNameText.StripGlobal( fullName );
            var core = TypeNameText.SplitArraySuffix( text, out _ );
            foreach( var item in FlattenName( core ) )
                yield return item;
        }
    }

    /// <summary>
    /// 递归展开类型名,产出头部与全部泛型实参
    /// </summary>
    private static IEnumerable<string> FlattenName( string text ) {
        var open = text.IndexOf( '<' );
        if( open < 0 ) {
            yield return text;
            yield break;
        }
        var close = TypeNameText.FindMatchingAngle( text, open );
        if( close < 0 ) {
            yield return text;
            yield break;
        }
        yield return text.Substring( 0, open );
        foreach( var argument in TypeNameText.SplitTopLevel( text.Substring( open + 1, close - open - 1 ) ) ) {
            foreach( var item in FlattenName( argument ) )
                yield return item;
        }
        foreach( var argument in CollectArguments( text.Substring( close + 1 ) ) ) {
            foreach( var item in FlattenName( argument ) )
                yield return item;
        }
    }

    /// <summary>
    /// 收集嵌套类型后续段的泛型实参
    /// </summary>
    private static IEnumerable<string> CollectArguments( string text ) {
        for( var index = 0; index < text.Length; index++ ) {
            if( text[index] != '<' )
                continue;
            var close = TypeNameText.FindMatchingAngle( text, index );
            if( close < 0 )
                yield break;
            foreach( var argument in TypeNameText.SplitTopLevel( text.Substring( index + 1, close - index - 1 ) ) )
                yield return argument;
            index = close;
        }
    }

    /// <summary>
    /// 拆出owner与末段类型名
    /// </summary>
    private static (string Owner, string MetadataName) Split( string fullName ) {
        var text = TypeNameText.StripGlobal( fullName );
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
        var segments = TypeNameText.StripGlobal( fullName ).Split( '.' );
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
    /// 类型引用名,已导入且无歧义时用短名,否则仅限定头部
    /// </summary>
    internal string Reference( ITypeSymbol type ) {
        if( type == null )
            throw new ArgumentNullException( nameof( type ) );
        if( type is IArrayTypeSymbol array )
            return Reference( array.ElementType ) + TypeNameText.ArraySuffix( array.Rank );
        if( type is not INamedTypeSymbol named )
            return GeneratedSource.Full( type );
        if( IsNullable( named ) )
            return Reference( named.TypeArguments[0] ) + "?";
        if( named.SpecialType != SpecialType.None )
            return GeneratedSource.Full( named );
        var namespaceName = NamespaceName( named );
        var chain = BuildChain( named );
        if( namespaceName != null && Imported( namespaceName ) && IsAmbiguous( namespaceName, ResolvingName( named ) ) == false )
            return chain;
        return TypeNameText.Qualify( namespaceName == null ? chain : namespaceName + "." + chain );
    }

    /// <summary>
    /// 构建类型链,嵌套类型逐层展开并递归缩短类型实参
    /// </summary>
    private string BuildChain( INamedTypeSymbol type ) {
        var builder = new StringBuilder();
        if( type.ContainingType != null )
            builder.Append( BuildChain( type.ContainingType ) ).Append( '.' );
        builder.Append( GeneratedSource.Short( type ) );
        if( type.IsGenericType ) {
            builder.Append( '<' );
            var arguments = type.TypeArguments;
            for( var index = 0; index < arguments.Length; index++ ) {
                if( index > 0 )
                    builder.Append( ", " );
                builder.Append( Reference( arguments[index] ) );
            }
            builder.Append( '>' );
        }
        return builder.ToString();
    }

    /// <summary>
    /// 是否可空值类型
    /// </summary>
    private static bool IsNullable( INamedTypeSymbol type ) {
        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    /// <summary>
    /// 类型所在命名空间,全局命名空间返回null
    /// </summary>
    private static string NamespaceName( INamedTypeSymbol type ) {
        var namespaceSymbol = type.ContainingNamespace;
        return namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace ? null : namespaceSymbol.ToDisplayString();
    }

    /// <summary>
    /// 全限定名引用名,已导入且无歧义时用短名;裸名原样返回
    /// </summary>
    internal string Reference( string fullName ) {
        if( fullName == null )
            throw new ArgumentNullException( nameof( fullName ) );
        var text = TypeNameText.StripGlobal( fullName );
        var core = TypeNameText.SplitArraySuffix( text, out var suffix );
        return ReferenceText( core ) + suffix;
    }

    /// <summary>
    /// 递归缩短泛型实参
    /// </summary>
    private string ReferenceText( string text ) {
        var open = text.IndexOf( '<' );
        if( open < 0 )
            return ReferenceCore( text );
        var close = TypeNameText.FindMatchingAngle( text, open );
        if( close < 0 )
            return TypeNameText.Qualify( text );
        var head = text.Substring( 0, open );
        var arguments = TypeNameText.SplitTopLevel( text.Substring( open + 1, close - open - 1 ) )
            .Select( Reference );
        return ReferenceCore( head ) + "<" + string.Join( ", ", arguments ) + ">"
            + ReferenceNested( text.Substring( close + 1 ) );
    }

    /// <summary>
    /// 缩短嵌套类型后续段的泛型实参
    /// </summary>
    private string ReferenceNested( string text ) {
        if( text.Length == 0 )
            return string.Empty;
        var builder = new StringBuilder( text.Length );
        var index = 0;
        while( index < text.Length ) {
            var open = text.IndexOf( '<', index );
            if( open < 0 ) {
                builder.Append( text, index, text.Length - index );
                break;
            }
            var close = TypeNameText.FindMatchingAngle( text, open );
            if( close < 0 ) {
                builder.Append( text, index, text.Length - index );
                break;
            }
            builder.Append( text, index, open - index + 1 );
            builder.Append( string.Join( ", ",
                TypeNameText.SplitTopLevel( text.Substring( open + 1, close - open - 1 ) ).Select( Reference ) ) );
            builder.Append( '>' );
            index = close + 1;
        }
        return builder.ToString();
    }

    /// <summary>
    /// 单个类型段短名化,未导入或有歧义时补 global:: 限定
    /// </summary>
    private string ReferenceCore( string segment ) {
        var ( owner, metadataName ) = Split( segment );
        if( owner == null )
            return segment;
        if( IsNamespace( owner ) == false ) {
            var nested = SplitNested( segment );
            if( nested == null || Imported( nested.Value.Owner ) == false || IsAmbiguous( nested.Value.Owner, nested.Value.Id ) )
                return TypeNameText.Qualify( segment );
            return nested.Value.Chain;
        }
        if( Imported( owner ) == false || IsAmbiguous( owner, metadataName ) )
            return TypeNameText.Qualify( segment );
        return metadataName;
    }

    /// <summary>
    /// 未参与建域的类型引用名,未导入或有歧义时保留全限定名
    /// </summary>
    internal string ReferenceExternal( string fullName ) {
        return Reference( fullName );
    }

    /// <summary>
    /// 命名空间是否已导入
    /// </summary>
    private bool Imported( string namespaceName ) {
        return _imports.Contains( namespaceName, StringComparer.Ordinal );
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
}
