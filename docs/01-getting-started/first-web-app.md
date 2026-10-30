# Your first web app

You will build a small ASP.NET Core site whose script is written in TypeScript, and see it served
as JavaScript. You need the .NET 10 SDK and a terminal. No Node.

```mermaid
flowchart LR
  A["1. Create the project"] --> B["2. Add the packages"]
  B --> C["3. Serve static files"]
  C --> D["4. Write app.ts"]
  D --> E["5. Run it"]
  E --> F["6. Edit and reload"]
```

## 1. Create the project

```sh
dotnet new web -n Hello
cd Hello
```

## 2. Add the packages

```sh
dotnet add package Extrode.Pare.Build
dotnet add package Extrode.Pare.AspNetCore
```

Open `Hello.csproj` and mark the build package private. This keeps the build step out of any
project that references yours:

```xml
<ItemGroup>
  <PackageReference Include="Extrode.Pare.Build" Version="0.6.1" PrivateAssets="all" />
  <PackageReference Include="Extrode.Pare.AspNetCore" Version="0.6.1" />
</ItemGroup>
```

Use the version `dotnet add package` wrote for you; `0.6.1` here is only an example.

What each does:

- `Extrode.Pare.Build` strips every `wwwroot/**/*.ts` when you run `dotnet build`.
- `Extrode.Pare.AspNetCore` strips again when you save a `.ts` while `dotnet watch` runs, so you
  see the change without a rebuild.

## 3. Serve static files

Replace `Program.cs` with:

```csharp
using Extrode.Pare.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddPareDevelopment();   // does nothing outside Development
var app = builder.Build();

app.UseDefaultFiles();          // "/" serves index.html
app.UseRouting();
app.MapStaticAssets();          // serves what the build registered, including Pare's output

app.Run();
```

## 4. Write some TypeScript

Create `wwwroot/index.html`:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Hello, Pare</title>
  <script type="module" src="/js/app.js"></script>
</head>
<body>
  <h1 id="greeting"></h1>
</body>
</html>
```

The page asks for `/js/app.js`. You will only write `app.ts`. Create `wwwroot/js/app.ts`:

```ts
interface Person {
  name: string;
}

function greet(person: Person): string {
  return `Hello, ${person.name}!`;
}

const heading = document.querySelector("#greeting")!;
heading.textContent = greet({ name: "Pare" });
```

## 5. Build and run

```sh
dotnet build
```

Look at what the build made:

```
obj/Debug/net10.0/pare/js/app.js
```

That is your script with the types gone. Open it and compare it with `app.ts`: same lines, same
columns.

```sh
dotnet run
```

Open the address it prints. You should see "Hello, Pare!". Your browser asked for `/js/app.js`
and the build had already registered the stripped file as that URL.

## 6. Edit and watch it reload

Stop the app and run it with hot reload:

```sh
dotnet watch
```

Change `"Pare"` in `app.ts` to your own name and save. The page reloads with the new text and the
app does not restart.

> `dotnet watch` runs no build when only a `.ts` changes. Without `AddPareDevelopment()` the
> `.js` would go stale. That one line is what keeps it fresh.

## 7. See an error

Add this to the end of `app.ts`, after a blank line:

```ts
enum Color { Red, Green }
```

Build again:

```
wwwroot/js/app.ts(12,1): error PARE100: 'enum' generates runtime code and cannot be erased
(use a 'const' object with 'as const' instead of 'enum')
```

It reads like a C# compiler error and points at the line. Under `dotnet watch` the same message
appears in the log and the old `.js` keeps being served. Replace the enum with the suggestion and
it builds again. All the codes are in [Error codes](../06-reference/error-codes.md).

## 8. Publish

```sh
dotnet publish -c Release
```

The published site holds `app.js` with `.br` and `.gz` copies. The `.ts` source is not published.
Set `PareKeepSources=true` if you want it there.

## Where next

- Scripts live outside `wwwroot`: [TypeScript outside wwwroot](../03-guides/typescript-outside-wwwroot.md)
- Catch type errors in the build: [Type checking in CI](../03-guides/type-checking.md)
- Understand the hot reload details: [Hot reload](../03-guides/hot-reload.md)
- Every setting: [MSBuild properties](../06-reference/msbuild-properties.md)
