#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

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
    /// 被排除命名空间的类型全限定名到别名
    /// </summary>
    private readonly Dictionary<string, string> _aliases;
    /// <summary>
    /// 供产物写入的导入清单,含别名声明
    /// </summary>
    private readonly string[] _declaredImports;
    /// <summary>
    /// 产物文件所在命名空间,空串表示全局命名空间,null表示未指定,不参与遮蔽检查
    /// </summary>
    private readonly string _fileNamespace;

    /// <summary>
    /// 按类型符号建域
    /// </summary>
    internal ImportScope( Compilation compilation, string[] frameworkImports, IEnumerable<ITypeSymbol> types )
        : this( CreateIndex( compilation ), frameworkImports, types ) {
        _compilation = compilation;
    }

    /// <summary>
    /// 按类型符号建域,名称探测走索引
    /// </summary>
    internal ImportScope( CompilationIndex index, string[] frameworkImports, IEnumerable<ITypeSymbol> types ) {
        _index = RequireIndex( index );
        RequireImports( frameworkImports );
        if( types == null )
            throw new ArgumentNullException( nameof( types ) );
        _ambiguousNames = new HashSet<string>( StringComparer.Ordinal );
        _aliases = new Dictionary<string, string>( StringComparer.Ordinal );
        var shortNames = new Dictionary<string, SortedSet<string>>( StringComparer.Ordinal );
        foreach( var type in Flatten( types ) ) {
            if( type == null )
                continue;
            var namespaceName = type.ContainingNamespace;
            if( namespaceName == null || namespaceName.IsGlobalNamespace )
                continue;
            var owner = namespaceName.ToDisplayString();
            var metadataName = ResolvingName( type );
            if( shortNames.TryGetValue( metadataName, out var owners ) == false ) {
                owners = new SortedSet<string>( StringComparer.Ordinal );
                shortNames.Add( metadataName, owners );
            }
            owners.Add( owner );
        }
        _imports = Build( index, frameworkImports, shortNames, _ambiguousNames, _aliases );
        _declaredImports = ComposeImports( _imports, _aliases );
    }

    /// <summary>
    /// 按全限定名建域
    /// </summary>
    internal ImportScope( Compilation compilation, string[] frameworkImports, IEnumerable<string> fullNames )
        : this( CreateIndex( compilation ), frameworkImports, fullNames ) {
        _compilation = compilation;
    }

    /// <summary>
    /// 按全限定名建域,名称探测走索引
    /// </summary>
    internal ImportScope( CompilationIndex index, string[] frameworkImports, IEnumerable<string> fullNames ) {
        _index = RequireIndex( index );
        RequireImports( frameworkImports );
        if( fullNames == null )
            throw new ArgumentNullException( nameof( fullNames ) );
        _ambiguousNames = new HashSet<string>( StringComparer.Ordinal );
        _aliases = new Dictionary<string, string>( StringComparer.Ordinal );
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
            if( shortNames.TryGetValue( metadataName, out var owners ) == false ) {
                owners = new SortedSet<string>( StringComparer.Ordinal );
                shortNames.Add( metadataName, owners );
            }
            owners.Add( owner );
        }
        _imports = Build( index, frameworkImports, shortNames, _ambiguousNames, _aliases );
        _declaredImports = ComposeImports( _imports, _aliases );
    }

    /// <summary>
    /// 当前编译单元,索引构造的实例为null
    /// </summary>
    internal Compilation Compilation => _compilation;

    /// <summary>
    /// 产物using清单
    /// </summary>
    internal string[] Imports => _declaredImports;

    /// <summary>
    /// 派生产物文件作用域,共享导入分析与别名,遮蔽检查按文件命名空间的外层链进行,
    /// 私有织入等写进用户命名空间的产物必须传入文件命名空间,全局命名空间产物传空串
    /// </summary>
    /// <param name="fileNamespace">产物文件命名空间,空串表示全局命名空间</param>
    internal ImportScope ForFile( string fileNamespace ) {
        return new ImportScope( this, fileNamespace );
    }

    /// <summary>
    /// 从既有作用域派生文件作用域
    /// </summary>
    private ImportScope( ImportScope source, string fileNamespace ) {
        _ambiguousNames = source._ambiguousNames;
        _imports = source._imports;
        _compilation = source._compilation;
        _index = source._index;
        _fileNamespace = fileNamespace;
        _aliases = FilterAliases( source._aliases, fileNamespace );
        _declaredImports = ComposeImports( _imports, _aliases );
    }

    /// <summary>
    /// 剔除与文件命名空间外层链同名的别名
    /// </summary>
    private Dictionary<string, string> FilterAliases( Dictionary<string, string> aliases, string fileNamespace ) {
        if( fileNamespace == null || aliases.Count == 0 )
            return aliases;
        var result = new Dictionary<string, string>( StringComparer.Ordinal );
        foreach( var pair in aliases ) {
            if( IsAliasTakenInFileChain( pair.Value, fileNamespace ) )
                continue;
            result.Add( pair.Key, pair.Value );
        }
        return result;
    }

    /// <summary>
    /// 别名是否命中文件命名空间链中的类型或子命名空间
    /// </summary>
    private bool IsAliasTakenInFileChain( string alias, string fileNamespace ) {
        var cursor = fileNamespace;
        while( cursor != null ) {
            if( _index.HasSimpleMember( cursor, alias ) || ExistsNestedNamespace( cursor, alias ) )
                return true;
            cursor = ParentNamespace( cursor );
        }
        return false;
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
    /// 加入框架清单与业务命名空间,自动排除有重名类型的业务命名空间
    /// </summary>
    private static string[] Build( CompilationIndex index, string[] frameworkImports, Dictionary<string, SortedSet<string>> shortNames,
            HashSet<string> ambiguousNames, Dictionary<string, string> aliases ) {
        var candidates = new SortedSet<string>( StringComparer.Ordinal );
        foreach( var pair in shortNames )
            foreach( var owner in pair.Value )
                candidates.Add( owner );
        var list = new List<string>( frameworkImports.Length );
        foreach( var name in frameworkImports )
            Add( list, name );
        var excluded = new HashSet<string>( StringComparer.Ordinal );
        foreach( var owner in candidates ) {
            if( list.Contains( owner, StringComparer.Ordinal ) )
                continue;
            if( HasNameConflict( index, list, owner ) ) {
                excluded.Add( owner );
                continue;
            }
            list.Add( owner );
        }
        foreach( var name in index.GetConflictingNames( list ) )
            ambiguousNames.Add( name );
        AddAliases( index, list, shortNames, excluded, aliases );
        return list.ToArray();
    }

    /// <summary>
    /// 追加命名空间,重复项忽略
    /// </summary>
    private static void Add( List<string> list, string namespaceName ) {
        if( namespaceName == null || list.Contains( namespaceName, StringComparer.Ordinal ) )
            return;
        list.Add( namespaceName );
    }

    /// <summary>
    /// 候选命名空间与已导入清单是否存在重名顶层类型
    /// </summary>
    private static bool HasNameConflict( CompilationIndex index, List<string> imported, string namespaceName ) {
        foreach( var member in index.GetNamespaceMembers( namespaceName ) )
            foreach( var existing in imported )
                if( index.HasNamespaceMember( existing, member ) )
                    return true;
        return false;
    }

    /// <summary>
    /// 为被排除命名空间下的简单类型分配确定性别名
    /// </summary>
    private static void AddAliases( CompilationIndex index, List<string> imported, Dictionary<string, SortedSet<string>> shortNames,
            HashSet<string> excluded, Dictionary<string, string> aliases ) {
        var used = new HashSet<string>( StringComparer.Ordinal );
        foreach( var pair in shortNames.OrderBy( item => item.Key, StringComparer.Ordinal ) ) {
            if( IsSimpleName( pair.Key ) == false )
                continue;
            foreach( var owner in pair.Value ) {
                if( excluded.Contains( owner ) == false || index.HasNamespaceMember( owner, pair.Key ) == false )
                    continue;
                aliases[owner + "." + pair.Key] = AllocateAlias( index, imported, used, owner, pair.Key );
            }
        }
    }

    /// <summary>
    /// 类型名是否可用别名表达
    /// </summary>
    private static bool IsSimpleName( string metadataName ) {
        return metadataName.Contains( "`" ) == false && metadataName.Contains( "<" ) == false
            && metadataName.Contains( "[" ) == false && metadataName.Contains( "." ) == false;
    }

    /// <summary>
    /// 分配不与已导入类型重名的别名
    /// </summary>
    private static string AllocateAlias( CompilationIndex index, List<string> imported, HashSet<string> used,
            string owner, string metadataName ) {
        var baseName = owner.Replace( ".", string.Empty ) + metadataName;
        var candidate = baseName;
        for( var suffix = 2; used.Contains( candidate ) || IsNameTaken( index, imported, candidate ); suffix++ )
            candidate = baseName + "_" + suffix.ToString( CultureInfo.InvariantCulture );
        used.Add( candidate );
        return candidate;
    }

    /// <summary>
    /// 名称是否已被已导入命名空间下的类型占用
    /// </summary>
    private static bool IsNameTaken( CompilationIndex index, List<string> imported, string name ) {
        foreach( var namespaceName in imported )
            if( index.HasNamespaceMember( namespaceName, name ) )
                return true;
        return false;
    }

    /// <summary>
    /// 组合命名空间清单与别名声明供产物写入
    /// </summary>
    private static string[] ComposeImports( string[] imports, Dictionary<string, string> aliases ) {
        var result = new List<string>( imports );
        foreach( var pair in aliases.OrderBy( item => item.Value, StringComparer.Ordinal ) )
            result.Add( pair.Value + " = global::" + pair.Key );
        return result.ToArray();
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
        if( _aliases.TryGetValue( TypeNameText.StripGlobal( GeneratedSource.Full( type ) ), out var alias ) )
            return alias;
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
        if( namespaceName != null && Imported( namespaceName ) && IsAmbiguous( namespaceName, ResolvingName( named ) ) == false
            && IsShadowedInFileChain( namespaceName, HeadOf( chain ) ) == false )
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
    /// 单个类型段短名化,未导入、有歧义或被文件命名空间外层链遮蔽时补 global:: 限定
    /// </summary>
    private string ReferenceCore( string segment ) {
        if( _aliases.TryGetValue( segment, out var alias ) )
            return alias;
        var ( owner, metadataName ) = Split( segment );
        if( owner == null ) {
            var globalHead = HeadOf( segment );
            return IsShadowedInFileChain( string.Empty, globalHead ) ? TypeNameText.Qualify( segment ) : segment;
        }
        if( IsNamespace( owner ) == false ) {
            var nested = SplitNested( segment );
            if( nested == null || Imported( nested.Value.Owner ) == false || IsAmbiguous( nested.Value.Owner, nested.Value.Id ) )
                return TypeNameText.Qualify( segment );
            var chain = nested.Value.Chain;
            if( IsShadowedInFileChain( nested.Value.Owner, HeadOf( chain ) ) )
                return TypeNameText.Qualify( segment );
            return chain;
        }
        if( Imported( owner ) == false || IsAmbiguous( owner, metadataName ) || IsShadowedInFileChain( owner, metadataName ) )
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

    /// <summary>
    /// 短名是否被产物文件命名空间外层链遮蔽,沿文件命名空间向全局逐级查重,
    /// 首个命中命名的层级若是目标类型自身所在命名空间则解析正确不算遮蔽,否则C#外层声明优先于using会被劫持
    /// </summary>
    private bool IsShadowedInFileChain( string owner, string shortName ) {
        if( _fileNamespace == null )
            return false;
        var simpleName = shortName.StartsWith( "@", StringComparison.Ordinal ) ? shortName.Substring( 1 ) : shortName;
        var cursor = _fileNamespace;
        while( cursor != null ) {
            if( ExistsSimpleMember( cursor, simpleName ) || ExistsNestedNamespace( cursor, simpleName ) )
                return cursor != owner;
            cursor = ParentNamespace( cursor );
        }
        return false;
    }

    /// <summary>
    /// 命名空间下是否存在指定名称的子命名空间
    /// </summary>
    private bool ExistsNestedNamespace( string namespaceName, string name ) {
        var fullName = namespaceName.Length == 0 ? name : namespaceName + "." + name;
        return _index.ContainsNamespace( fullName );
    }

    /// <summary>
    /// 命名空间是否包含指定简单名的顶层类型,泛型成员按反引号前 arity 归一化匹配
    /// </summary>
    private bool ExistsSimpleMember( string namespaceName, string simpleName ) {
        return _index.HasSimpleMember( namespaceName, simpleName );
    }

    /// <summary>
    /// 取父命名空间,顶层命名空间的父级为全局命名空间空串,全局命名空间再无父级返回null
    /// </summary>
    private static string ParentNamespace( string namespaceName ) {
        if( namespaceName.Length == 0 )
            return null;
        var dot = namespaceName.LastIndexOf( '.' );
        return dot > 0 ? namespaceName.Substring( 0, dot ) : string.Empty;
    }

    /// <summary>
    /// 取渲染链的首段,遮蔽检查按简单名解析起点进行
    /// </summary>
    private static string HeadOf( string renderedName ) {
        var dot = renderedName.IndexOf( '.' );
        return dot > 0 ? renderedName.Substring( 0, dot ) : renderedName;
    }
}
