# 主世界来货

为 Probably Stolen 0.46D 制作的本地模组，加入 Minecraft 原版村民顾客、物品、矿区、合成、储物箱和自动熔炉。

Steam 创意工坊：<https://steamcommunity.com/sharedfiles/filedetails/?id=3844140125>

源代码：<https://github.com/AdAstra888/MinecraftCrossing>

代码按 MIT License 开源。`Textures/` 中的 Minecraft 风格资源用于本模组，Minecraft 及其相关资产的权利归 Mojang Studios / Microsoft 所有；发布或再分发前请确认你遵守适用的 Minecraft 使用规则。

## 安装

1. 备份游戏存档。
2. 将压缩包内的文件解压到游戏目录下的 `Mods/minecraft_crossing` 文件夹。`manifest.xml` 应直接位于该文件夹内。
3. 在游戏的模组管理界面启用“主世界来货”，然后重启游戏。

## 停用或移除

在游戏模组管理界面停用模组并重启；需要完全移除时，再删除 `Mods/minecraft_crossing` 文件夹。重新安装时把文件夹放回原处、启用并重启即可。

存档中带有模组物品时，移除后游戏可能把它们显示为未知物品；模组箱子或熔炉里的内容也依赖该模组。移除前请先备份存档，并把箱子、熔炉中的物品取出。重新启用模组后再载入备份，可恢复完整功能和贴图。

## 内容

- 煤炭、木炭、矿石、锭、方块、工具、食物和唱片。
- 面包带有原版热量数据，可进入当铺的动物饲料流程，也可从农夫顾客的货品中购买。
- 农夫供货顾客使用 Minecraft Java 1.20.1 原版平原农夫村民贴图。
- 村民立绘按当铺原版顾客的 288×211 画布和 100 PPU 规则制作，以 Minecraft 原版模型 UV 像素呈现头部、交叉手臂和上半身近景；角色占幅略小于精灵框。
- 末影人作为低概率拾荒顾客出现，沿用原版拾荒者商品池、议价和交易流程，使用 Minecraft Java 1.20.1 原版贴图。
- 开发者可在 F9 的顾客查看器中搜索 `mc_enderman_scavenger` 并点“Queue next”立即测试末影人；精灵查看器搜索相同键可单独预览贴图。
- 工作台左侧是配方目录，右侧显示产物数量、现有材料和缺少数量，可直接合成。
- 可升级的免费起始储物箱。
- 按真实秒计时、睡眠模拟时继续推进的自动熔炉。
- 夜间矿区按出门携带的最高等级镐子计数：石镐 2 次、铁镐 3 次、钻石镐 4 次。石镐可从垃圾场找到。

## 物品操作

- 普通拖动移动整组物品。
- 按住 Shift 拖动一半；按住 Alt 拖动 1 个。
- 普通材料最多堆叠 64 个；唱片最多 16 个；镐子和熔炉不堆叠。

## 开发测试

开发者可在已载入存档时按 F9，进入 `Spawn` 并打开 `Open Debug Spawner`。搜索物品的完整 ID（例如 `mc_bread`），单击生成 1 个；按住 Shift 单击生成 10 个。F2 可复制鼠标悬停物品的 ID。游戏目录的空 `dev.txt` 文件只用于启用开发工具，不要随模组发布。

打包前可在 PowerShell 运行 `.\tools\Validate-Content.ps1`，检查物品 ID、占格与贴图画布、中英文名称和说明、配方材料/产物、熔炼关系及矿区掉落引用。

## 内容扩展

物品基础资料位于 `Content/items.csv`，格式为 `id,sprite,value_tier,width,height,game_types`。类型使用竖线分隔，并采用当铺原版类型，例如 `FOOD`、`MATERIAL`、`TOOL`、`LUXURY_ITEM`。普通顾客按这些类型挑选收购商品，Minecraft 物品因此可进入对应原版顾客的购买范围。新 ID 使用 `mc_` 前缀；贴图放在 `Textures/Items/<sprite>.png`。游戏每格为 16 像素，因此贴图画布应为 `width×16` 乘 `height×16`。完整图案应按比例放入画布，不裁剪、不拉伸。

配方位于 `Content/recipes.csv`：产物 ID、产量、本地化名称键、本地化说明键，后面可继续添加任意多组“材料 ID、数量”。熔炼关系位于 `Content/smelting.csv`，每行填写输入和输出 ID。中文、英文文本分别写入 `Localization/zh.csv` 和 `Localization/en.csv`。新材料可直接通过数据加入；新机器或物品专属行为需要扩展 `ModEntry.cs`。

矿区掉落位于 `LootTables/overworld.json`，由游戏直接加载。给对应镐子的 `entries` 添加物品 ID 和正数权重即可；掉落概率约为该物品的权重除以池内总权重。文件中的 `junkTable` 条目把石镐和工作台加入原版废料掉落。
