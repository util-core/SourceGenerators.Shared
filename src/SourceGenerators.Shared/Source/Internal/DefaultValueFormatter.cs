#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SourceGenerators.Shared.Internal;

/// <summary>
/// 参数默认值字面量输出
/// </summary>
internal static class DefaultValueFormatter {
    /// <summary>
    /// 无法表达的默认值,输出时省略
    /// </summary>
    internal const string Omitted = "";

    /// <summary>
    /// 输出参数默认值;无默认值返回null,值类型的 default 字段输出 default
    /// </summary>
    internal static string Format( IParameterSymbol parameter ) {
        if( parameter == null )
            throw new ArgumentNullException( nameof( parameter ) );
        if( parameter.HasExplicitDefaultValue == false )
            return null;
        var value = parameter.ExplicitDefaultValue;
        if( value == null )
            return IsNonNullableValueType( parameter.Type ) ? "default" : "null";
        return FormatValue( value, parameter.Type );
    }

    /// <summary>
    /// 是否非可空值类型
    /// </summary>
    private static bool IsNonNullableValueType( ITypeSymbol type ) {
        if( type.IsValueType == false )
            return false;
        return ( type as INamedTypeSymbol )?.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    /// <summary>
    /// 按配置文本和目标类型输出字面量;无法解析返回null
    /// </summary>
    internal static string Format( string text, ITypeSymbol type ) {
        if( type == null )
            throw new ArgumentNullException( nameof( type ) );
        type = UnwrapNullable( type );
        var value = Parse( text, type );
        if( value == null && type.SpecialType != SpecialType.System_String )
            return null;
        return FormatValue( value, type );
    }

    /// <summary>
    /// 解包可空类型,取基础类型
    /// </summary>
    private static ITypeSymbol UnwrapNullable( ITypeSymbol type ) {
        if( type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T )
            return named.TypeArguments[0];
        return type;
    }

    /// <summary>
    /// 按目标类型解析配置文本
    /// </summary>
    private static object Parse( string text, ITypeSymbol type ) {
        if( text == null )
            return null;
        if( type.TypeKind == TypeKind.Enum )
            return ParseEnum( text, type );
        switch( type.SpecialType ) {
            case SpecialType.System_String:
                return text;
            case SpecialType.System_Boolean:
                return bool.TryParse( text, out var flag ) ? flag : (object)null;
            case SpecialType.System_Char:
                return text.Length == 1 ? text[0] : (object)null;
            case SpecialType.System_SByte:
                return sbyte.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i8 ) ? i8 : (object)null;
            case SpecialType.System_Byte:
                return byte.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u8 ) ? u8 : (object)null;
            case SpecialType.System_Int16:
                return short.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i16 ) ? i16 : (object)null;
            case SpecialType.System_UInt16:
                return ushort.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u16 ) ? u16 : (object)null;
            case SpecialType.System_Int32:
                return int.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32 ) ? i32 : (object)null;
            case SpecialType.System_UInt32:
                return uint.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u32 ) ? u32 : (object)null;
            case SpecialType.System_Int64:
                return long.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64 ) ? i64 : (object)null;
            case SpecialType.System_UInt64:
                return ulong.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u64 ) ? u64 : (object)null;
            case SpecialType.System_Single:
                return float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f32 ) ? f32 : (object)null;
            case SpecialType.System_Double:
                return double.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f64 ) ? f64 : (object)null;
            case SpecialType.System_Decimal:
                return decimal.TryParse( text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d128 ) ? d128 : (object)null;
        }
        return null;
    }

    /// <summary>
    /// 解析枚举成员名或整数
    /// </summary>
    private static object ParseEnum( string text, ITypeSymbol type ) {
        foreach( var member in type.GetMembers() ) {
            if( member is IFieldSymbol field && field.HasConstantValue && string.Equals( field.Name, text, StringComparison.Ordinal ) )
                return field.ConstantValue;
        }
        return long.TryParse( text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) ? value : (object)null;
    }

    /// <summary>
    /// 输出特性实参字面量;无法表达返回null
    /// </summary>
    internal static string Format( TypedConstant constant ) {
        if( constant.IsNull )
            return "null";
        if( constant.Kind == TypedConstantKind.Type )
            return constant.Value is ITypeSymbol type ? "typeof( " + GeneratedSource.Full( type ) + " )" : "null";
        if( constant.Kind == TypedConstantKind.Array )
            return FormatArray( constant );
        var value = FormatValue( constant.Value, constant.Type );
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// 输出数组实参字面量;任一元素无法表达时返回null
    /// </summary>
    private static string FormatArray( TypedConstant constant ) {
        var items = new List<string>();
        foreach( var item in constant.Values ) {
            var value = Format( item );
            if( value == null )
                return null;
            items.Add( value );
        }
        var elementType = ( constant.Type as IArrayTypeSymbol )?.ElementType;
        if( elementType == null )
            return null;
        return "new " + GeneratedSource.Full( elementType ) + "[] { " + string.Join( ", ", items ) + " }";
    }

    /// <summary>
    /// 按字面量类型输出C#表达式;无法表达返回空字符串
    /// </summary>
    internal static string FormatValue( object value, ITypeSymbol type ) {
        if( type == null )
            return string.Empty;
        type = UnwrapNullable( type );
        if( value is bool flag )
            return flag ? "true" : "false";
        if( value is string text )
            return Quote( text );
        if( value is char character )
            return Literal( character );
        if( type.TypeKind == TypeKind.Enum )
            return "( " + GeneratedSource.Full( type ) + " )" + ToInteger( value );
        if( value is float single )
            return Floating( single, float.IsNaN( single ), float.IsPositiveInfinity( single ), float.IsNegativeInfinity( single ) );
        if( value is double number )
            return Floating( number, double.IsNaN( number ), double.IsPositiveInfinity( number ), double.IsNegativeInfinity( number ) );
        if( value is decimal money )
            return money.ToString( CultureInfo.InvariantCulture ) + "m";
        if( value is long longValue )
            return longValue.ToString( CultureInfo.InvariantCulture ) + "L";
        if( value is ulong unsignedLong )
            return unsignedLong.ToString( CultureInfo.InvariantCulture ) + "UL";
        if( value is uint unsignedInt )
            return unsignedInt.ToString( CultureInfo.InvariantCulture ) + "U";
        if( value is byte || value is sbyte || value is short || value is ushort || value is int )
            return ToInteger( value );
        return string.Empty;
    }

    /// <summary>
    /// 字符字面量
    /// </summary>
    private static string Literal( char value ) {
        var builder = new StringBuilder( 6 ).Append( '\'' );
        AppendChar( builder, value, '\'' );
        return builder.Append( '\'' ).ToString();
    }

    /// <summary>
    /// 整数统一按long输出;ulong超出long范围时原样输出
    /// </summary>
    private static string ToInteger( object value ) {
        if( value is ulong unsignedLong )
            return unsignedLong.ToString( CultureInfo.InvariantCulture );
        try {
            return Convert.ToInt64( value, CultureInfo.InvariantCulture ).ToString( CultureInfo.InvariantCulture );
        }
        catch {
            return Convert.ToString( value, CultureInfo.InvariantCulture );
        }
    }

    /// <summary>
    /// 单精度浮点字面量,NaN与无穷用静态成员
    /// </summary>
    private static string Floating( float value, bool nan, bool positive, bool negative ) {
        if( nan )
            return "float.NaN";
        if( positive )
            return "float.PositiveInfinity";
        if( negative )
            return "float.NegativeInfinity";
        return value.ToString( "R", CultureInfo.InvariantCulture ) + "f";
    }

    /// <summary>
    /// 双精度浮点字面量,NaN与无穷用静态成员
    /// </summary>
    private static string Floating( double value, bool nan, bool positive, bool negative ) {
        if( nan )
            return "double.NaN";
        if( positive )
            return "double.PositiveInfinity";
        if( negative )
            return "double.NegativeInfinity";
        return value.ToString( "R", CultureInfo.InvariantCulture ) + "d";
    }

    /// <summary>
    /// 字符串字面量
    /// </summary>
    internal static string Quote( string text ) {
        var builder = new StringBuilder( text.Length + 2 ).Append( '"' );
        foreach( var item in text ) {
            AppendChar( builder, item, '"' );
        }
        return builder.Append( '"' ).ToString();
    }

    /// <summary>
    /// 追加转义字符
    /// </summary>
    private static void AppendChar( StringBuilder builder, char item, char quote ) {
        switch( item ) {
            case '\\':
                builder.Append( "\\\\" );
                return;
            case '\'':
                if( quote == '\'' )
                    builder.Append( "\\'" );
                else
                    builder.Append( item );
                return;
            case '"':
                if( quote == '"' )
                    builder.Append( "\\\"" );
                else
                    builder.Append( item );
                return;
            case '\0':
                builder.Append( "\\0" );
                return;
            case '\a':
                builder.Append( "\\a" );
                return;
            case '\b':
                builder.Append( "\\b" );
                return;
            case '\f':
                builder.Append( "\\f" );
                return;
            case '\n':
                builder.Append( "\\n" );
                return;
            case '\r':
                builder.Append( "\\r" );
                return;
            case '\t':
                builder.Append( "\\t" );
                return;
            case '\v':
                builder.Append( "\\v" );
                return;
        }
        if( char.IsControl( item ) || char.IsSurrogate( item ) || item == '\u2028' || item == '\u2029' ) {
            builder.Append( "\\u" ).Append( ( (int)item ).ToString( "X4", CultureInfo.InvariantCulture ) );
            return;
        }
        builder.Append( item );
    }
}
