/// <summary>
/// 兼容壳：旧菜单/旧命令的入口，实际逻辑已统一到 GameBuilder。
/// 保留它是为了让历史命令 `-executeMethod QuickBuild.BuildWindows` 仍然可用；
/// 菜单项已移除，避免与 Build → 打包 Windows x64 重复。
/// </summary>
public static class QuickBuild
{
    public static void BuildWindows()
    {
        GameBuilder.BuildInteractive();
    }
}
