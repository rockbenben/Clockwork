using Clockwork.Core;
using Xunit;

public class StartupApprovedTests
{
    [Fact] public void Null_blob_enabled() => Assert.True(StartupApproved.IsApprovedEnabled(null));
    [Fact] public void Empty_blob_enabled() => Assert.True(StartupApproved.IsApprovedEnabled(Array.Empty<byte>()));
    [Fact] public void Blob_2_enabled() => Assert.True(StartupApproved.IsApprovedEnabled(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    [Fact] public void Blob_3_disabled() => Assert.False(StartupApproved.IsApprovedEnabled(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));

    [Fact] public void Blob_enable_is_2() => Assert.Equal((byte)2, StartupApproved.ApprovedBlob(true)[0]);
    [Fact] public void Blob_disable_is_3() => Assert.Equal((byte)3, StartupApproved.ApprovedBlob(false)[0]);
    [Fact] public void Blob_len_12() => Assert.Equal(12, StartupApproved.ApprovedBlob(true).Length);

    [Fact] public void TypeLabel_registry() => Assert.Equal("注册表", StartupLabels.TypeLabel("Registry"));
    [Fact] public void TypeLabel_task() => Assert.Equal("计划任务", StartupLabels.TypeLabel("ScheduledTask"));
    [Fact] public void ScopeLabel_machine_admin() => Assert.Equal("所有用户（需管理员）", StartupLabels.ScopeLabel("Machine", true));
    [Fact] public void ScopeLabel_user() => Assert.Equal("当前用户", StartupLabels.ScopeLabel("User", false));
}
