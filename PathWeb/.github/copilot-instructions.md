# Copilot Instructions

## Project Guidelines
- User's Azure SQL Database: Server: labconfig.database.windows.net, Database: LabConfig. Tables needed: Config, PublicIP, Regions, Settings, Tenant, Users. User connects with Entra ID auth using jonor@microsoft.com account. Always use modern Entra ID-based authentication (Managed Identity, delegated tokens, or DefaultAzureCredential) for Azure DevOps authentication. Never suggest or use PATs (Personal Access Tokens).
- The workspace root is Q:\Bin\git\Projects (the Git repo), but the PathWeb project is at Q:\Bin\git\Projects\PathWeb. When creating files, use paths relative to the project folder like "Areas/About/Views/Home/Lab.cshtml" instead of "PathWeb/Areas/About/Views/Home/Lab.cshtml". The create_file tool uses the project folder as the base, so file paths should not include the "PathWeb/" prefix. Including this prefix will create a nested PathWeb/PathWeb directory. The replace_string_in_file and get_file tools can find existing files with either path format.

## File Manipulation Guidelines
- When using replace_string_in_file, always include enough surrounding context (the full HTML tag/line at minimum) to avoid matching just a substring inside an attribute value and corrupting the markup.