using System;

namespace AutoDeleteImage
{
    /// <summary>
    /// 删除任务配置项
    /// </summary>
    [Serializable]
    public class TaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新任务";
        public string FolderPath { get; set; } = "";
        public string FileExtensions { get; set; } = ".jpg,.png,.bmp,.txt,.csv"; // 逗号分隔，不含点或含点均可
        public int RetentionDays { get; set; } = 7; // 保留天数
        public bool Enabled { get; set; } = true;
        public DateTime LastRunTime { get; set; } = DateTime.MinValue;
        public int DeletedCount { get; set; } = 0;
        public string LastError { get; set; } = "";

        // ==================== 新增：触发条件字段 ====================
        // 触发模式：0=手动/定时，1=电脑空闲时，2=磁盘空余不足时，3=定时时间点
        public int TriggerMode { get; set; } = 0; // 0=定时/手动，1=空闲，2=磁盘空余，3=定时时间
        public int IdleMinutes { get; set; } = 30; // 电脑空闲多少分钟后执行
        public string ScheduleTime { get; set; } = "02:00"; // 每天执行时间 HH:mm
        public bool EnableDiskThreshold { get; set; } = false; // 是否启用磁盘空余检测
        public double DiskFreeThresholdGB { get; set; } = 10.0; // 磁盘空余低于多少GB时触发
        public string TargetDriveLetter { get; set; } = "C"; // 监控的盘符

        public TaskItem() { }

        public TaskItem Clone()
        {
            return new TaskItem
            {
                Id = this.Id,
                Name = this.Name,
                FolderPath = this.FolderPath,
                FileExtensions = this.FileExtensions,
                RetentionDays = this.RetentionDays,
                Enabled = this.Enabled,
                LastRunTime = this.LastRunTime,
                DeletedCount = this.DeletedCount,
                LastError = this.LastError,
                TriggerMode = this.TriggerMode,
                IdleMinutes = this.IdleMinutes,
                ScheduleTime = this.ScheduleTime,
                EnableDiskThreshold = this.EnableDiskThreshold,
                DiskFreeThresholdGB = this.DiskFreeThresholdGB,
                TargetDriveLetter = this.TargetDriveLetter
            };
        }

        public string DisplaySummary
        {
            get
            {
                string triggerDesc;
                if (TriggerMode == 0)
                    triggerDesc = "定时/手动";
                else if (TriggerMode == 1)
                    triggerDesc = $"空闲{IdleMinutes}分";
                else if (TriggerMode == 2)
                    triggerDesc = $"磁盘<{DiskFreeThresholdGB}GB";
                else if (TriggerMode == 3)
                    triggerDesc = $"每日{ScheduleTime}";
                else
                    triggerDesc = "未知";

                return $"{Name} | {FolderPath} | {FileExtensions} | 保留{RetentionDays}天 | 触发:{triggerDesc} | {(Enabled ? "启用" : "禁用")}";
            }
        }
    }
}
