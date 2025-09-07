# Development Instructions for Claude

## Build Requirements
After completing a unit of work, you MUST build, commit, and push so that your co-developer can test the changes in their environment.
1. **Always build after every change** - Run `dotnet build src/` after making any code changes
2. **Zero errors and zero warnings** - The build must pass with 0 errors and 0 warnings before proceeding
3. **Commit and push when build passes** - Only commit and push changes after achieving a clean build

## Build Command
```bash
dotnet build src/
```

## User Interface Guidelines
- **Always use canonical MudBlazor components** - Never invent or use non-existent component properties
- **Follow MudBlazor documentation** - Refer to official MudBlazor docs for correct component usage
- **Use MudBlazor styles** - Utilize MudBlazor's built-in CSS classes and theming system

## Code Quality Standards
- Fix all nullable reference warnings by properly declaring nullable types
- Initialize all non-nullable fields and properties
- Handle all possible null references
- Follow C# naming conventions and best practices