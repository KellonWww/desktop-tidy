# Desktop Tidy 设计约定

参考：../personal-blog/DESIGN.md 与 src/styles/globals.css，仅参考，不修改博客。

## 视觉语言

| 主题 | 底色 | 卡片 | 强调色 |
|---|---|---|---|
| 夜航 | #142126 | #1b2b31 | #d5f578 |
| 日光 | #f0ecdf | #f8f5eb | #284ab3 |

- 跟随 Windows 应用深浅色；保留系统高对比模式，不增加第三套主题。
- 颜色集中在 Theme.Palette；线条、文字、弱强调与博客 token 对齐。
- 控件圆角 10px、分类卡片 18px；细描边、纯色表面，不使用渐变光晕或投影。
- 侧栏字标 Tidy.，内容区采用标题、真实分类/图标数量与细线分隔。
- 中文使用既有 Microsoft YaHei UI，英文字标使用 Segoe UI；不请求网络字体。
- 搜索、拖放、删除、撤销、滚动、透明度与桌面挂载行为保持不变。
- 仅改现有 WinForms 绘制层，不迁移到 WebView，不引入 UI 依赖。
