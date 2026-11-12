# SourceGenerators.Shared

源生成器共享源码包。包内只有 `.cs` 源码、`buildTransitive` props 与 README，不产生 `lib/`、`ref/` 或 `analyzers/` 资产。源码以 `internal` 直接编入生成器项目，不形成运行时依赖，也不需要 `InternalsVisibleTo`。

## 功能

- **源码注入**：props 把共享源码编入当前生成器项目，类型均为 `internal`，不形成运行时程序集依赖。
- **名称索引**：`CompilationIndex` 缓存编译单元的命名空间与顶层类型，提供类型存在性、命名空间成员和跨命名空间重名探测。
- **类型引用与 using 管理**：`ImportScope` 按本次生成实际用到的类型构建 `using` 清单；短名优先，自动处理命名空间冲突、确定性别名和产物文件命名空间遮蔽。
- **源码拼装**：`GeneratedSource` 负责文件头、文件级命名空间、`using` 块、模块初始化器骨架，以及完整名、定义名、短名和引用名输出。
- **生成文件名**：`HintNameAllocator` 统一分配唯一 `.g.cs` 文件名，净化非法字符并处理撞名。
- **默认值字面量**：`DefaultValueFormatter` 把参数默认值、特性实参和配置文本输出为 C# 字面量，覆盖枚举、可空类型、浮点特殊值与字符串/字符转义。
- **类型名文本**：`TypeNameText` 提供 `global::` 前缀、数组后缀、泛型实参拆分和尖括号匹配等工具。

## 使用

在**源生成器项目**引用包：

```xml
<PackageReference Include="SourceGenerators.Shared" Version="1.0.0" PrivateAssets="all" />
```

`PrivateAssets="all"` 防止该包进入生成器发布后的依赖。

props 会把以下源码编入当前项目，命名空间为 `SourceGenerators.Shared.Internal`，类型均为 `internal`：

```
SourceGenerators.Shared\Usings.cs
SourceGenerators.Shared\Internal\CompilationIndex.cs
SourceGenerators.Shared\Internal\ImportScope.cs
SourceGenerators.Shared\Internal\GeneratedSource.cs
SourceGenerators.Shared\Internal\HintNameAllocator.cs
SourceGenerators.Shared\Internal\DefaultValueFormatter.cs
SourceGenerators.Shared\Internal\TypeNameText.cs
```

`Usings.cs` 是全局 using，不属于 `SourceGenerators.Shared.Internal` 命名空间，会作用于当前项目。

### 项目

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IncludeBuildOutput>false</IncludeBuildOutput>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="SourceGenerators.Shared" Version="1.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### 生成器

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SourceGenerators.Shared.Internal;

namespace Demo;

[Generator]
public sealed class CustomerGenerator : IIncrementalGenerator {
    public void Initialize( IncrementalGeneratorInitializationContext context ) {
        context.RegisterSourceOutput( context.CompilationProvider, static ( context, compilation ) => {
            var index = CompilationIndex.From( compilation, context.CancellationToken );
            if( index.TypeExists( "Demo.Customer" ) == false )
                return;
            var customer = compilation.GetTypeByMetadataName( "Demo.Customer" );
            if( customer == null )
                return;
            var scope = new ImportScope( index, new[] { "System" }, new ITypeSymbol[] { customer } )
                .ForFile( "Demo.Generated" );
            var customerType = scope.Reference( customer );
            var body = $"internal static class CustomerProbe {{ internal static System.Type Type => typeof( {customerType} ); }}";
            var source = GeneratedSource.Wrap( body, scope.Imports, "Demo.Generated" );
            context.AddSource( "CustomerProbe.g.cs", SourceText.From( source, Encoding.UTF8 ) );
        } );
    }
}
```

`ImportScope` 的第二个参数是预置 `using`，第三个参数是本次生成用到的类型；`ForFile` 传入产物文件命名空间，`GeneratedSource.Wrap` 负责文件头、命名空间和 `using`。

类型引用统一通过 `scope.Reference(...)` 生成，不要手写短名：只有经过 `Reference`，冲突别名和文件命名空间遮蔽回退才会生效。

### 产物文件命名空间

- 生成到独立命名空间（例如 `Demo.Generated`）：建议传入实际文件命名空间，避免已有同名类型造成错误绑定。
- 私有织入到用户命名空间：必须传入该命名空间，例如 `.ForFile( "Your.Product" )`。
- 生成到全局命名空间：传入空串 `.ForFile( "" )`。
- 不调用 `ForFile`：不做遮蔽检查，保持传统短名行为。

## 兼容性

- 源码使用 `netstandard2.0` 可用的 BCL API 和 C# 10 语法；
- 测试项目使用 **net8.0 + C# 10 + Microsoft.CodeAnalysis.CSharp 4.8.0** 验证，更新版本同样可用；
- 消费方至少需要 C# 10。

## API 说明

### CompilationIndex

`CompilationIndex.From( compilation, cancellationToken )` 遍历当前编译及其引用程序集，建立顶层类型与命名空间索引；同一 `Compilation` 实例的结果通过 `ConditionalWeakTable` 缓存，可重复调用。已有索引时优先使用 `ImportScope( index, ... )` 构造重载，避免重复遍历。

- `TypeExists( "Demo.Customer" )`：仅当该元数据名在编译单元中唯一存在时返回 `true`。
- `ContainsNamespace` / `HasNamespaceMember` / `HasSimpleMember`：命名空间与顶层成员探测；`HasSimpleMember` 按反引号前的简单名匹配泛型类型。
- `GetNamespaceMembers` / `GetConflictingNames`：读取命名空间顶层类型清单、找出多个命名空间之间的重名类型。
- `AssemblyName`：当前编译程序集名。

索引只收集顶层类型；嵌套类型通过其外层类型参与判定。

### ImportScope

`ImportScope` 是类型引用和 `using` 管理的核心。

#### 构造

可用构造为 `Compilation` / `CompilationIndex` 与 `ITypeSymbol` / 全限定名字符串的组合：

- 类型符号重载会展开数组元素和泛型实参；
- 字符串重载会展开数组后缀、泛型实参与嵌套类型链；
- 第二个参数是预置框架 `using` 清单，按调用方给定顺序优先加入，且不做冲突检查；
- 第三个参数是本次生成用到的类型，用于构建业务命名空间清单、冲突检测和别名分配。

#### using 清单

`scope.Imports` 返回组合好的 `using` 指令数组，可直接传给 `GeneratedSource.Wrap` 或 `AppendImports`。别名以 `Alias = global::Full.Name` 形式追加在末尾。

命名空间冲突自动处理：业务命名空间按 Ordinal 排序后依次尝试加入，与已加入清单（框架清单或更早加入的业务命名空间）存在重名顶层类型时，整个命名空间被排除。被排除命名空间下实际用到的简单类型生成确定性别名，例如：

```csharp
// Lib.A 与 Lib.B 都有 Widget 时
// using Lib.A;                              // 保留优先者
// using LibBWidget = global::Lib.B.Widget;  // 被排除侧生成别名
// Reference: Widget / LibBWidget
```

别名只覆盖简单类型名；泛型、数组与嵌套类型在被排除命名空间下仍输出 `global::` 全限定名。别名与已导入类型重名时追加 `_2`、`_3` 序号。框架 `using` 清单由调用方负责，内部即使存在重名类型也原样保留。

#### 文件命名空间

`ForFile( fileNamespace )` 派生一个共享导入清单和别名缓存的作用域，用于检查产物文件命名空间外层链上的同名类型：

- 用户命名空间：`ForFile( "Your.Product" )`；
- 全局命名空间：`ForFile( "" )`；
- 不调用：不做遮蔽检查。

`Reference` 会沿文件命名空间向全局逐级查找同名顶层类型；若先命中的层级不是目标类型自身所在命名空间，引用回退为 `global::` 全限定。

```csharp
// 产物文件命名空间为 Sample，且 Sample 中存在 MethodInterceptionContext
var scope = new ImportScope( index, new[] { "System" }, new ITypeSymbol[] { context } )
    .ForFile( "Sample" );
// Reference: global::CallSiteAop.MethodInterceptionContext
```

#### 类型引用

`scope.Reference( type )` 和 `scope.Reference( "Full.Type.Name" )` 输出引用名：

- 数组、多维数组、可空类型、泛型与嵌套类型递归处理；
- 命名空间已导入、短名无歧义且未被文件命名空间遮蔽时使用短名；
- 否则只限定头部，泛型实参与嵌套链继续缩短；
- 关键字标识符输出为 `@class` 形式。

```csharp
// using Lib; 时
// Outer<int>.Inner<string>

// Lib 未导入、存在短名冲突或被文件命名空间遮蔽时
// global::Lib.Outer<int>.Inner<string>
```

嵌套泛型会完整保留外层类型及其实参。`System` 的 `using` 不会自动覆盖 `System.Text` 等子命名空间，子命名空间需要显式加入框架 using 清单，或由引用类型自动带入。

`ReferenceExternal( "Full.Type.Name" )` 与字符串 `Reference` 等价，用于未参与建域的类型；未导入时保留全限定名。

### GeneratedSource

`GeneratedSource.Wrap` 的生成命名空间由调用方传入：

```csharp
var text = GeneratedSource.Wrap( inner, imports, "YourProduct.Internal.Generated" );
```

- `AppendImports( builder, imports )`：单独写入 `using` 块，空清单不写内容；
- `AppendInitializer( builder, className, classSummary, registerSummary, lines )`：写入 `internal static class` 与 `[ModuleInitializer]` 注册方法骨架；
- `Full( type )`：`global::` 全限定名，特殊类型输出关键字；
- `FullDefinition( type )`：不含类型参数的泛型定义名；
- `Short( type )`：仅名称，关键字标识符转义；
- `Reference( type/fullName, imports )`：短名优先引用，但不做冲突检测、别名或文件命名空间遮蔽处理；需要这些规则时使用 `ImportScope.Reference`。

`AppendInitializer` 输出的代码使用 `[ModuleInitializer]`，调用方需确保产物 `using` 清单包含 `System.Runtime.CompilerServices`，且目标框架支持模块初始化器。

### HintNameAllocator

`HintNameAllocator` 统一分配生成文件名：短名后接 `.g.cs`，非法字符转为下划线；撞名时在扩展名前追加 `_2`、`_3` 序号。需要预留的固定文件名通过构造参数传入，同样按短名书写。

```csharp
var allocator = new HintNameAllocator( new[] { "SharedInitializer" } );
var fileName = allocator.Allocate( "Order" ); // Order.g.cs
context.AddSource( fileName, SourceText.From( source, Encoding.UTF8 ) );
```

### DefaultValueFormatter

`DefaultValueFormatter` 输出 C# 字面量：

- `Format( parameter )`：无显式默认值返回 `null`；显式默认值为 `null` 时，非可空值类型输出 `default`，引用类型与可空类型输出 `null`；无法表达返回 `Omitted`。
- `Format( text, type )`：按目标类型解析配置文本，解析失败返回 `null`；字符串类型的 `null` 文本返回 `Omitted`。
- `Format( constant )`：输出特性实参，支持 `typeof( ... )`、数组与 `null`；无法表达返回 `null`。
- `FormatValue( value, type )`：底层字面量输出，不支持的值返回 `Omitted`。
- `Quote( text )`：字符串字面量转义。

可空类型按基础类型解析和输出（例如 `Level?` 的默认值仍保留枚举转换）；字符串与字符字面量会转义控制字符、行分隔符与代理项；数值解析使用 `InvariantCulture`。

### TypeNameText

- `StripGlobal` / `Qualify`：去掉或补上 `global::`；
- `ArraySuffix( rank )` / `SplitArraySuffix`：生成或拆分数组后缀，多维输出 `[,]`；
- `SplitTopLevel` / `FindMatchingAngle`：拆分泛型实参、匹配尖括号。

## 作者

何镇汐

## 许可

MIT
