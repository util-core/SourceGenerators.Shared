# SourceGenerators.Shared

源生成器共享源码包。包内只有 `.cs` 源码和 `buildTransitive` props，不产生 `lib/`、`ref/` 或 `analyzers/` 资产。

## 使用

在**源生成器项目**引用包：

```xml
<PackageReference Include="SourceGenerators.Shared" Version="1.0.0-preview.1" PrivateAssets="all" />
```

`PrivateAssets="all"` 防止该包进入生成器发布后的依赖。

props 会把以下源码编入当前项目，命名空间为 `SourceGenerators.Shared.Internal`，类型均为 `internal`：

```
SourceGenerators.Shared\Usings.cs
SourceGenerators.Shared\Internal\CompilationIndex.cs
SourceGenerators.Shared\Internal\GeneratedSource.cs
SourceGenerators.Shared\Internal\ImportScope.cs
SourceGenerators.Shared\Internal\DefaultValueFormatter.cs
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
    <PackageReference Include="SourceGenerators.Shared" Version="1.0.0-preview.1" PrivateAssets="all" />
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
            var scope = new ImportScope( compilation, new[] { "System" }, new ITypeSymbol[] { customer } );
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

`CompilationIndex.From( compilation )` 建立名称索引，用于 `TypeExists`、`ContainsNamespace`、`HasNamespaceMember` 探测。

### ImportScope

`ImportScope` 按生成产物实际引用的类型动态构建 `using` 清单，并规避短名歧义。

互斥命名空间用于避免同名类型冲突，由消费方按需传入：

```csharp
var scope = new ImportScope( compilation, frameworkImports, types, new[] { "Legacy.Helpers" } );
```

多个互斥命名空间之间采用“先进入清单者优先”的策略，后续冲突项直接忽略，相关类型由 `Reference` 保留全限定名。

### GeneratedSource

`GeneratedSource.Wrap` 的生成命名空间由调用方传入：

```csharp
var text = GeneratedSource.Wrap( inner, imports, "YourProduct.Internal.Generated" );
```

### DefaultValueFormatter

`DefaultValueFormatter.Format( parameter )` 输出参数默认值字面量；无法表达时返回 `null`，或使用 `DefaultValueFormatter.Omitted` 省略默认值。

## 作者

何镇汐

## 许可

MIT
