using Core.Event;
using Core.Models.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Servicers.Interfaces
{
    /// <summary>
    /// 应用配置
    /// </summary>
    public interface IAppConfig
    {
        /// <summary>
        /// 加载配置
        /// </summary>
        void Load();

        /// <summary>
        /// 配置是否已成功加载（未加载前禁止 Save，防止用 null/空对象覆写配置文件）
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 获取配置
        /// </summary>
        /// <returns></returns>
        ConfigModel GetConfig();

        /// <summary>
        /// 更新配置
        /// </summary>
        void Save();

        /// <summary>
        /// 重建默认配置（配置缺失/损坏时兜底）
        /// </summary>
        void CreateDefaultConfig();
        /// <summary>
        /// 配置修改时发生
        /// </summary>
        event AppConfigEventHandler ConfigChanged;
        /// <summary>
        /// 数据存储目录变更时发生，需要重启软件后完全生效
        /// </summary>
        event Action StoragePathChanged;
    }
}
