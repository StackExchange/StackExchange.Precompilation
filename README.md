StackExchange.Precompilation
============================

Replacing csc.exe
-----------------

- `Install-Package StackExchange.Precompilation.Build -Pre`

Replacing aspnet_compiler.exe for .cshtml precompilation
--------------------------------------------------------

- `Install-Package StackExchange.Precompilation.Build -Pre`
- Add `<PropertyGroup><SEPrecompilerIncludeRazor>true</SEPrecompilerIncludeRazor></PropertyGroup>` to your .csproj file (usually replacing the `MvcBuildViews` property)

#### Using precompiled views

- [Replace the MvcWebRazorHostFactory](https://github.com/StackExchange/StackExchange.Precompilation/blob/master/Test.WebApp/Views/Web.config#L12)
- [Add the PrecompiledViewEngine to ViewEngines](https://github.com/StackExchange/StackExchange.Precompilation/blob/master/Test.WebApp/Global.asax.cs#L33)

Meta-programming
----------------

- Create a new project 
- `Install-Package StackExchange.Precompilation.Metaprogramming -Pre`
- Implement the ICompileModule interface
- `Install-Package StackExchange.Precompilation.Build -Pre` in the target project
- [Configure your new module](https://github.com/StackExchange/StackExchange.Precompilation/blob/master/Test.ConsoleApp/App.config#L8) in the target project's web.config or app.config


Development
-----------

if you have an existing project with StackExchange.Precompilation packages and encounter a bug you can simply:

- pull this repo
- increment semver.txt
- make the fix in the source code
- run BuildAndPack.ps1 (requires a console with VS env vars in your PATH, I recommend powershell with Posh-VsVars)
- setup a nuget source pointing at .\packages\obj
- after that you can update the packages StackExchange.Precompilation in your target project from the packages\obj source
- this gives you local *-alpha{timestamp} packages instead of the public *-beta{build} ones
- PROTIP: if you want to attach an debugger to the compilation of your project or any of the Test.* projects, add a `System.Diagnostics.Debugger.Launch()` statement somewhere in the code ;)