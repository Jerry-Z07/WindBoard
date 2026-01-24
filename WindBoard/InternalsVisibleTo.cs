using System.Runtime.CompilerServices;

// 允许测试项目访问内部类型，避免为了测试而把实现细节改成 public。
[assembly: InternalsVisibleTo("WindBoard.Tests")]

