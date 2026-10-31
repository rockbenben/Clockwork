using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Views;
using Button = System.Windows.Controls.Button;

namespace Clockwork;

// 逐个功能：**每一种步骤类型都过一遍**，把「这一类是不是真的能用」从「看代码觉得能」变成「跑过」。
//
// 两条线：
//   ① 编辑器：这类型能不能开出一扇编辑器（XAML 与绑定在这一类上炸过、该露的面板没露出来）。
//   ② 引擎：这类型是不是真的被派发（引擎的 default 分支会报「不认识的步骤类型」——
//      配置里写错一个 kind 就是静默不执行，正是本仓库最怕的那种故障：
//      「写下这一行的人以为自己收窄了某一种类型，实际什么也没发生」）。
//
// **跑的是「无害配置」**：空目标、找不到的进程、空组合……每一种都让引擎在动手之前就报一句，
// 于是既证明了派发到了那一支，又不会在用户的机器上真开一个程序、真发一个键、真滚一下滚轮。
//
// 少数几类**没有无害配置**（鼠标、获取选中的文字），它们在用户的桌面上真的会动手：
// 这两类不跑，理由写在这里，由它们各自的单测兜着。
internal static class FuncSweep
{
    /// <summary>跑一遍逐功能自查。返回是否全过；<paramref name="log"/> 是给人看的那份账。</summary>
    public static bool Run(out string log)
    {
        var fails = new List<string>();
        int checkedKinds = 0;

        foreach (var kind in StepDisplay.StepKinds)
        {
            if (!Harmless.TryGetValue(kind, out var step)) continue;   // 跑不得的那几类：见类头
            try
            {
                // ① 编辑器：这类型开得出来吗
                var w = new StepEditorWindow(step, Array.Empty<ActionGroup>());
                try
                {
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Left = -32000; w.Top = -32000; w.ShowActivated = false;
                    w.Show();
                    PanelKeyChecks.Pump(w, 40);
                    if (!TypeDropdownHas(w, kind))
                        fails.Add($"{kind}：编辑器的类型下拉里没有它");
                }
                finally { try { w.Close(); } catch { } }

                // ② 引擎：它被派发了吗（不是那句「不认识的步骤类型」）
                var r = StepRunner.InvokeStepAction(step, confirmDestructive: null, selfPaths: Array.Empty<string>());
                string unknown = Strings.Lf("Warn_UnknownKind", kind);
                if (r.HasWarning && r.Warning == unknown)
                    fails.Add($"{kind}：引擎不认识它（静默不执行）");
                checkedKinds++;
            }
            catch (Exception ex) { fails.Add($"{kind}：{ex.GetType().Name} {ex.Message}"); }
        }

        // 编排层派发的那三类（每个 / 列表 / 动作组）走的是 ActionGroupRunner，不是引擎——
        // 引擎不认识它们是**对的**（在引擎里单跑一个组没有意义）。这里用编排层验一遍。
        try
        {
            var vars = new RunVars();
            var deps = new GroupDeps
            {
                Vars = vars,
                AskUser = _ => "第一行\n第二行",          // 问句那条链的桩：答一个固定的
                RunStep = s => StepRunner.InvokeStepAction(s, null, Array.Empty<string>(), null, vars),
                OnStepError = (_, ex) => fails.Add("编排层抛了：" + ex.Message),
            };
            var group = new ActionGroup
            {
                Id = "sweep",
                Steps = new()
                {
                    new LaunchStep { Kind = "each" },                       // 没有孩子 → 跑 0 遍
                    new LaunchStep { Kind = "list", OutputVar = "L" },      // 问一句 → 落进变量
                    new LaunchStep { Kind = "group", GroupId = "没有这个组" }, // 找不到的组 → 如实说一声
                },
            };
            ActionGroupRunner.RunGroup(group, deps);
            if (vars.Get("L") == "") fails.Add("列表：问句的答案没落进变量");
        }
        catch (Exception ex) { fails.Add("编排层：" + ex.Message); }

        log = fails.Count == 0
            ? $"逐功能自查：{checkedKinds} 种类型全过（另 3 种由编排层派发）"
            : string.Join("；", fails);
        return fails.Count == 0;
    }

    // 编辑器的「类型」下拉里有没有这一类（下拉是窗口里第一个 ComboBox）。
    private static bool TypeDropdownHas(Window w, string kind)
    {
        var combo = FindFirst<ComboBox>(w);
        if (combo == null) return false;
        string label = StepDisplay.StepKindLabel(kind);
        return combo.Items.OfType<ComboBoxItem>().Any(i => (i.Content as string) == label)
               || combo.Items.OfType<string>().Any(s => s == label);
    }

    private static T? FindFirst<T>(DependencyObject root) where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            var deep = FindFirst<T>(c);
            if (deep != null) return deep;
        }
        return null;
    }

    // 每种类型一条「无害配置」：跑它只会得到一句「这一步没法用」，不会在用户的机器上动手。
    private static readonly Dictionary<string, LaunchStep> Harmless = new()
    {
        ["app"] = new LaunchStep { Kind = "app", Target = "" },                       // 空目标 → 报「没填」
        ["url"] = new LaunchStep { Kind = "url", Target = "" },
        ["path"] = new LaunchStep { Kind = "path", Target = "" },
        ["keys"] = new LaunchStep { Kind = "keys", Combo = "!!!" },                  // 认不出的组合 → 报「无法识别的键」，不发键
        ["text"] = new LaunchStep { Kind = "text", Text = "" },
        ["volume"] = new LaunchStep { Kind = "volume", VolumeAction = "没有这个动作" },
        ["window"] = new LaunchStep { Kind = "window", WindowAction = "close", Process = "__不存在的进程__" },
        // 「关机」配 confirmDestructive=null → 无人值守不弹确认，于是它**拒绝执行**：
        // 既证明了派发到了系统命令那一支，又不会把用户的机器关掉。
        ["system"] = new LaunchStep { Kind = "system", Command = "shutdown" },
        ["waitClipboard"] = new LaunchStep { Kind = "waitClipboard", WaitSeconds = 1 },  // 等 1 秒就超时
        ["browserUrl"] = new LaunchStep { Kind = "browserUrl" },                     // 只读浏览器地址
        ["siteMap"] = new LaunchStep { Kind = "siteMap", Text = "" },               // 没网址 → 如实说一声
        ["prompt"] = new LaunchStep { Kind = "prompt", Message = "问一句？" },       // 无界面 → 报「这一条没有界面」
        ["choice"] = new LaunchStep { Kind = "choice", Message = "选一个？" },
        ["delay"] = new LaunchStep { Kind = "delay", DelayMs = 10 },
        ["message"] = new LaunchStep { Kind = "message", Message = "一句话" },        // 非交互路径静默跳过
        ["regex"] = new LaunchStep { Kind = "regex", Pattern = "", Text = "abc" },
        ["replace"] = new LaunchStep { Kind = "replace", Pattern = "", Text = "abc" },
        ["urlDecode"] = new LaunchStep { Kind = "urlDecode", Text = "%41" },
        ["join"] = new LaunchStep { Kind = "join", Text = "" },
        ["splitLines"] = new LaunchStep { Kind = "splitLines", Text = "a\nb" },
        ["math"] = new LaunchStep { Kind = "math", Text = "1+1" },
        ["skip"] = new LaunchStep { Kind = "skip" },
        ["waitKey"] = new LaunchStep { Kind = "waitKey", Text = "" },               // 没有键名 → 立刻报「没有可等的键」
    };

    // 这两类**没有无害配置**：跑它们会在用户的桌面上真的动手——
    //   mouse：动作没有无效值（未知动作回落到第一个 = 真滚一格）；copySelection：会往当前前台窗口按 Ctrl+C。
    // 它们的实现另有单测兜着（Win32MouseButtonMappingTests / MouseUpLedgerTests / CopyComboTests）。
    public static readonly string[] NotRunnable = { "mouse", "copySelection" };
}
