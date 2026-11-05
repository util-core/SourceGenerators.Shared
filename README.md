# SourceGenerators.Shared

源生成器共享源码包。包内只有 `.cs` 源码和 `buildTransitive` props，不产生 `lib/`、`ref/` 或 `analyzers/` 资产。

## 使用

在**源生成器项目**引用包：

```xml
<PackageReference Include="SourceGenerators.Shared" Version="1.0.0-preview.4" PrivateAssets="all" />
```

`PrivateAssets="all"` 防止该包进入生成器发布后的依赖。

props 会把以下源码编入当前项目，命名空间为 `SourceGenerators.Shared.Internal`，类型均为 `internal`：

```
SourceGenerators.Shared\Usings.cs
SourceGenerators.Shared\Internal\CompilationIndex.cs
SourceGenerators.Shared\Internal\GeneratedSource.cs
SourceGenerators.Shared\Internal\HintNameAllocator.cs
SourceGenerators.Shared\Internal\ImportScope.cs
SourceGenerators.Shared\Internal\DefaultValueFormatter.cs
SourceGenerators.Shared\Internal\TypeNameText.cs
```

`Usings.cs` 是全局 using，会作用于当前项目。

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
    <PackageReference Include="SourceGenerators.Shared" Version="1.0.0-preview.4" PrivateAssets="all" />
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
            var scope = new ImportScope( index, new[] { "System" }, new ITypeSymbol[] { customer } );
            var body = "internal static class CustomerProbe { internal static System.Type Type => typeof( Customer ); }";
            var source = GeneratedSource.Wrap( body, scope.Imports, "Demo.Generated" );
            context.AddSource( "CustomerProbe.g.cs", SourceText.From( source, Encoding.UTF8 ) );
        } );
    }
}
```

`ImportScope` 的第二个参数是预置 `using`，第三个参数是本次生成用到的类型；`GeneratedSource.Wrap` 负责文件头、命名空间和 `using`。

## 兼容性

- 源码使用 `netstandard2.0` 可用的 BCL API 和 C# 10 语法；
- 测试项目使用 **net8.0 + C# 10 + Microsoft.CodeAnalysis.CSharp 4.8.0** 验证，更新版本同样可用；
- 消费方至少需要 C# 10。

## API 说明

### CompilationIndex

`CompilationIndex.From( compilation )` 建立名称索引，用于 `TypeExists`、`ContainsNamespace`、`HasNamespaceMember` 探测。同一 `Compilation` 实例的索引会被缓存，可重复调用；已有索引时优先使用 `ImportScope( index, ... )` 构造重载，避免重复遍历。

### ImportScope

`ImportScope` 按生成产物实际引用的类型动态构建 `using` 清单，并规避短名歧义。短名是优先结果：只有命名空间未导入或短名冲突时，才把头部限定为全名，泛型实参与嵌套类型链仍尽量使用短名。例如：

```csharp
// using Lib; 时
// Outer<int>.Inner<string>

// Lib 未导入或 Outer 冲突时
// global::Lib.Outer<int>.Inner<string>
```

嵌套泛型会完整保留外层类型及其实参；关键字标识符输出为 `@class` 形式。`System` 的 `using` 不会自动覆盖 `System.Text` 等子命名空间，子命名空间需要显式加入框架 using 清单或由引用类型自动带入。

命名空间冲突自动处理：候选命名空间与已导入清单存在重名顶层类型时，自动只保留先进入清单者，被排除命名空间下实际用到的简单类型生成确定性别名，其余类型保留全限定名。例如：

```csharp
// Lib.A 与 Lib.B 都有 Widget 时
// using Lib.A;                       // 保留优先者
// using LibBWidget = global::Lib.B.Widget;   // 被排除侧生成别名
// Reference: Widget / LibBWidget
```

别名只覆盖简单类型名；泛型与嵌套类型在被排除命名空间下仍输出 `global::` 全限定名。框架 using 清单由调用方负责，内部即使存在重名类型也原样保留。

### GeneratedSource

`GeneratedSource.Wrap` 的生成命名空间由调用方传入：

```csharp
var text = GeneratedSource.Wrap( inner, imports, "YourProduct.Internal.Generated" );
```

`GeneratedSource.Reference` 同样遵循“短名优先”：命名空间已导入时使用短名，未导入时只限定头部，类型实参与嵌套链继续缩短。

### HintNameAllocator

`HintNameAllocator` 统一分配生成文件名：短名后接 `.g.cs`，非法字符转为下划线；撞名时在扩展名前追加 `_2`、`_3` 序号。需要预留的固定文件名通过构造参数传入，同样按短名书写。

```csharp
var allocator = new HintNameAllocator( new[] { "SharedInitializer" } );
var fileName = allocator.Allocate( "Order" );
context.AddSource( fileName, SourceText.From( source, Encoding.UTF8 ) );
```

### DefaultValueFormatter

`DefaultValueFormatter.Format( parameter )` 输出参数默认值字面量；无法表达时返回 `null`，或使用 `DefaultValueFormatter.Omitted` 省略默认值。可空类型按基础类型解析和输出（例如 `Level?` 的默认值仍保留枚举转换），字符串与字符字面量会转义控制字符、行分隔符与代理项。

## 作者

何镇汐

## 许可

MIT
