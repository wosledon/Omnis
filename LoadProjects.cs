using System;

// 获取当前目录以及子目录所有的 .csproj 文件
string[] csprojFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.AllDirectories);
foreach (string csprojFile in csprojFiles)
{
    Console.WriteLine($"Found .csproj file: {csprojFile}");
    // 这里可以添加你想要对每个 .csproj 文件进行的操作
}
// 对每个csproj文件执行 dotnet sln add 命令
foreach (string csprojFile in csprojFiles)
{
    string command = $"dotnet sln add \"{csprojFile}\"";
    Console.WriteLine($"Executing command: {command}");
    var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }
    };
    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    Console.WriteLine(output);
}
