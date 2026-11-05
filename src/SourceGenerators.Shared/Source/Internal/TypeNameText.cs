namespace SourceGenerators.Shared.Internal;

/// <summary>
/// 类型名字符串处理
/// </summary>
internal static class TypeNameText {
    /// <summary>
    /// 去掉 global:: 前缀
    /// </summary>
    internal static string StripGlobal( string text ) {
        return text.StartsWith( "global::", StringComparison.Ordinal ) ? text.Substring( 8 ) : text;
    }

    /// <summary>
    /// 补全 global:: 前缀
    /// </summary>
    internal static string Qualify( string text ) {
        return text.StartsWith( "global::", StringComparison.Ordinal ) ? text : "global::" + text;
    }

    /// <summary>
    /// 分离数组后缀,返回核心类型名
    /// </summary>
    internal static string SplitArraySuffix( string text, out string suffix ) {
        var end = text.Length;
        while( end >= 2 && text[end - 2] == '[' && text[end - 1] == ']' )
            end -= 2;
        suffix = text.Substring( end );
        return text.Substring( 0, end );
    }

    /// <summary>
    /// 按顶层逗号拆分泛型实参
    /// </summary>
    internal static IEnumerable<string> SplitTopLevel( string body ) {
        var depth = 0;
        var start = 0;
        for( var index = 0; index < body.Length; index++ ) {
            var current = body[index];
            if( current == '<' )
                depth++;
            else if( current == '>' )
                depth--;
            else if( current == ',' && depth == 0 ) {
                yield return body.Substring( start, index - start ).Trim();
                start = index + 1;
            }
        }
        var last = body.Substring( start ).Trim();
        if( last.Length > 0 )
            yield return last;
    }

    /// <summary>
    /// 查找与左尖括号匹配的右尖括号,未找到返回 -1
    /// </summary>
    internal static int FindMatchingAngle( string text, int openIndex ) {
        var depth = 0;
        for( var index = openIndex; index < text.Length; index++ ) {
            var current = text[index];
            if( current == '<' )
                depth++;
            else if( current == '>' ) {
                depth--;
                if( depth == 0 )
                    return index;
            }
        }
        return -1;
    }
}
