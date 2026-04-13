using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// 允许测试项目访问内部类型，避免为了测试而把实现细节改成 public。
[assembly: InternalsVisibleTo("WindBoard.Tests")]

// 修复 CA1416：CrashReporter 是纯 Windows WinForms 程序，在程序集级别标注平台，
// 告知分析器所有 WinForms API 调用在目标平台上均可用。
[assembly: SupportedOSPlatform("windows")]


