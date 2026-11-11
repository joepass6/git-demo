/// <summary>
/// 兼容壳：旧命令行入口 `-executeMethod BuildGame.BuildStandalone`，已转发到 GameBuilder.Build。
/// 输出目录不再是 D:\测试\Win64，统一到 GameBuilder.DefaultOutputDir（D:\BuildOut）。
/// </summary>
public static class BuildGame
{
    public static void BuildStandalone()
    {
        GameBuilder.Build();
    }
}
