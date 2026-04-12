# .NET 8 Cross-Platform Building Guide

This guide provides the compilation commands and environment setup for Windows, Linux, and macOS (Apple Silicon).

## 📥 .NET 8 Download Links

If you want use .NET 8 runtime and the target machine does not have installed, use these links:

- **Windows/Linux/OSX:** [.NET 8 Desktop/Server Runtime Downloads]([Download .NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime))
  
  

---

## 1. Windows (x64)

| **Type**                              | **Command**                                                                             |
| ------------------------------------- | --------------------------------------------------------------------------------------- |
| **Framework Dependent** (No Runtime)  | `dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true` |
| **Self-Contained** (Includes Runtime) | `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`  |

---

## 2. Linux (x64)

| **Type**                              | **Command**                                                                               |
| ------------------------------------- | ----------------------------------------------------------------------------------------- |
| **Framework Dependent** (No Runtime)  | `dotnet publish -c Release -r linux-x64 --self-contained false /p:PublishSingleFile=true` |
| **Self-Contained** (Includes Runtime) | `dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true`  |

---

## 3. macOS (Apple Silicon / M1, M2, M3)

| **Type**                              | **Command**                                                                               |
| ------------------------------------- | ----------------------------------------------------------------------------------------- |
| **Framework Dependent** (No Runtime)  | `dotnet publish -c Release -r osx-arm64 --self-contained false /p:PublishSingleFile=true` |
| **Self-Contained** (Includes Runtime) | `dotnet publish -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true`  |

---

## 💡 Important Notes for Linux & macOS

### 1. Assign Execution Permissions

Unlike Windows, you must manually grant execution rights to the binary:

Bash

```
# Replace 'MyApp' with your actual file name
chmod +x ./MyApp
```

### 2. How to Run

Execute the file by referencing the current directory:

Bash

```
./MyApp
```

### 3. File Extensions

- **Windows:** Output is `MyApp.exe`.

- **Linux/macOS:** Output is a binary with **no file extension**.

### 4. Scripting (.sh files)

To automate this, use a shell script with **LF** line endings and a Shebang:

Bash

```
#!/bin/bash
dotnet publish -c Release -r linux-x64 --self-contained false /p:PublishSingleFile=true
```

### 5. Dependency Requirements

- **Framework Dependent:** Small file size, but requires the [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to be installed on the target.

- **Self-Contained:** Larger file size (all DLLs included), but runs out-of-the-box without any .NET installation.