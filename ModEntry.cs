using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinecraftCrossing
{
    public sealed class ModEntry : IMod
    {
        private const string ModId = "minecraft_crossing";
        private const string MineId = ModId + ".overworld_mine";
        private const string TableStone = ModId + ".mine_stone";
        private const string TableIron = ModId + ".mine_iron";
        private const string TableDiamond = ModId + ".mine_diamond";
        private const string StarterChestGrantedKey = "starter_mc_chest_granted";
        private const string VillagerSpriteKey = "mc_villager_farmer";
        private const string EndermanSpriteKey = "mc_enderman_scavenger";
        private const string EndermanCustomerId = "mc_enderman_scavenger";
        private const int FurnaceSecondsPerOre = 10;
        private const int FurnaceSecondsPerFuel = 8 * FurnaceSecondsPerOre;
        private const string LegacyFurnaceHeatTag = "minecraft_crossing.furnace_heat";
        private const string FurnaceClockTag = "minecraft_crossing.furnace_clock";
        private static readonly Dictionary<string, int> PriceTiers = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "A", 5 }, { "B", 20 }, { "C", 90 }, { "D", 200 }, { "E", 500 }
        };
        private ModLog log;
        private Action refreshMinePanel;
        private Func<StoreClient> originalFarmerSupplier;
        private Func<StoreClient> originalScavengerFactory;
        private RandomClientPool.Entry scavengerPoolEntry;
        private UnityEngine.Sprite villagerSprite;
        private UnityEngine.Sprite endermanSprite;
        private bool villagerSpriteRegistered;
        private bool endermanSpriteRegistered;
        private bool endermanDebugCustomerRegistered;
        private int farmerSupplierIndex = -1;
        private readonly List<ItemDefinition> items = new List<ItemDefinition>();
        private readonly List<SmeltingDefinition> smeltingRecipes = new List<SmeltingDefinition>();

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log("Minecraft Java 1.20.1 content module initialized.");
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += RegisterItems;
            ModHook.OnModAssetsLoaded += SetMinecraftSpriteFiltering;
            ModHook.OnGameLoadedInit += RegisterVillagerSprite;
            ModHook.OnGameLoadedInit += RegisterEndermanSprite;
            ModHook.OnGameLoadedInit += GrantStarterChest;
            ModHook.OnGameLoadedLate += SyncLoadedFootprints;
            RegisterFarmerBreadStock();
            RegisterEndermanScavenger();

            DelegateAfterhourLocation mine = ModHelper.AddAfterhourLocation(
                ModId,
                MineId,
                title: () => ModHelper.GetLocalized(ModId, "afterhour_mines_title"),
                onLeave: () => refreshMinePanel = null,
                buildPanel: BuildMinePanel,
                isDiscovered: () => true,
                isAvailable: () => true);

            if (mine != null)
            {
                mine.usesAfterhourBag = true;
                mine.onTick = () => refreshMinePanel?.Invoke();
            }
            log.Log("Enabled item registration and the Overworld Mine location.");
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnModAssetsLoaded -= SetMinecraftSpriteFiltering;
            ModHook.OnGameLoadedInit -= RegisterVillagerSprite;
            ModHook.OnGameLoadedInit -= RegisterEndermanSprite;
            ModHook.OnGameLoadedInit -= GrantStarterChest;
            ModHook.OnGameLoadedLate -= SyncLoadedFootprints;
            UnregisterFarmerBreadStock();
            UnregisterEndermanScavenger();
            UnregisterVillagerSprite();
            UnregisterEndermanSprite();
            ModHelper.RemoveAfterhourLocations(ModId);
            refreshMinePanel = null;
            log?.Log("Disabled.");
        }

        private void RegisterFarmerBreadStock()
        {
            if (originalFarmerSupplier != null)
                return;

            for (int i = 0; i < StoreClientList.sellClientList.Count; i++)
            {
                if (StoreClientList.sellClientList[i] != (Func<StoreClient>)StoreClientList.CreateFarmerClientStandard)
                    continue;

                farmerSupplierIndex = i;
                originalFarmerSupplier = StoreClientList.sellClientList[i];
                StoreClientList.sellClientList[i] = CreateFarmerWithMinecraftBread;
                log.Log("Added Minecraft bread to the existing farmer supplier.");
                return;
            }

            log.Warning("Could not find the farmer supplier entry. Minecraft bread will have no regular customer source.");
        }

        private StoreClient CreateFarmerWithMinecraftBread()
        {
            RegisterVillagerSprite();
            StoreClient farmer = originalFarmerSupplier();
            if (farmer != null)
                farmer.spriteName = VillagerSpriteKey;
            if (farmer != null && farmer.mainDialogue != null)
                farmer.mainDialogue.endAction += () => PlayerStore.Instance.AddDirectSellingItemToTable("mc_bread");
            return farmer;
        }

        private void RegisterVillagerSprite()
        {
            if (villagerSprite == null || SpriteDict.Instance == null || SpriteDict.Instance.spriteDictionary == null)
                return;

            if (!SpriteDict.Instance.spriteDictionary.ContainsKey(VillagerSpriteKey))
            {
                SpriteDict.Instance.spriteDictionary.Add(VillagerSpriteKey, villagerSprite);
                villagerSpriteRegistered = true;
                log.Log("Registered the original Minecraft 1.20.1 villager farmer sprite for customer display.");
            }
        }

        private void RegisterEndermanScavenger()
        {
            if (scavengerPoolEntry != null)
                return;

            foreach (RandomClientPool.Entry entry in StoreClientList.generalPool.entries)
            {
                if (entry.create != (Func<StoreClient>)StoreClientList.CreateScavGeneral)
                    continue;

                scavengerPoolEntry = entry;
                originalScavengerFactory = entry.create;
                entry.create = CreateOccasionalEnderman;
                StoreClientListDict.storeClientDict[EndermanCustomerId] = CreateEndermanScavenger;
                endermanDebugCustomerRegistered = true;
                log.Log("Added the Enderman as a rare variation of the existing scavenger customer.");
                return;
            }

            log.Warning("Could not find the standard scavenger customer pool entry. Enderman customer will not appear.");
        }

        private StoreClient CreateOccasionalEnderman()
        {
            if (RNG.Roll(10))
                return CreateEndermanScavenger();

            return originalScavengerFactory();
        }

        private StoreClient CreateEndermanScavenger()
        {
            RegisterEndermanSprite();
            StoreClient enderman = originalScavengerFactory();
            if (enderman == null)
                return null;

            enderman.identifier = EndermanCustomerId;
            enderman.displayName = ModHelper.GetLocalized(ModId, "enderman_customer_name");
            enderman.spriteName = EndermanSpriteKey;
            enderman.cooldown = 7;
            enderman.AddBasicDialog();
            enderman.mainDialogue = new Dialogue();
            enderman.mainDialogue
                .SetText(enderman.displayName, ModHelper.GetLocalized(ModId, "enderman_customer_greeting"))
                .NextDialogue()
                .SetText(enderman.displayName, ModHelper.GetLocalized(ModId, "enderman_customer_offer"))
                .SetEndAction(() =>
                {
                    StoreClientList.AddScavGeneralStock();
                    enderman.OnIntroduced();
                });
            return enderman;
        }

        private void UnregisterEndermanScavenger()
        {
            if (scavengerPoolEntry != null && scavengerPoolEntry.create == (Func<StoreClient>)CreateOccasionalEnderman)
                scavengerPoolEntry.create = originalScavengerFactory;

            scavengerPoolEntry = null;
            originalScavengerFactory = null;

            if (endermanDebugCustomerRegistered)
                StoreClientListDict.storeClientDict.Remove(EndermanCustomerId);
            endermanDebugCustomerRegistered = false;
        }

        private void UnregisterVillagerSprite()
        {
            if (villagerSpriteRegistered && SpriteDict.Instance != null && SpriteDict.Instance.spriteDictionary != null)
                SpriteDict.Instance.spriteDictionary.Remove(VillagerSpriteKey);

            villagerSpriteRegistered = false;
            if (villagerSprite != null)
            {
                UnityEngine.Object.Destroy(villagerSprite);
                villagerSprite = null;
            }
        }

        private void RegisterEndermanSprite()
        {
            if (endermanSprite == null || SpriteDict.Instance == null || SpriteDict.Instance.spriteDictionary == null)
                return;

            if (!SpriteDict.Instance.spriteDictionary.ContainsKey(EndermanSpriteKey))
            {
                SpriteDict.Instance.spriteDictionary.Add(EndermanSpriteKey, endermanSprite);
                endermanSpriteRegistered = true;
                log.Log("Registered the original Minecraft 1.20.1 Enderman sprite for customer display.");
            }
        }

        private void UnregisterEndermanSprite()
        {
            if (endermanSpriteRegistered && SpriteDict.Instance != null && SpriteDict.Instance.spriteDictionary != null)
                SpriteDict.Instance.spriteDictionary.Remove(EndermanSpriteKey);

            endermanSpriteRegistered = false;
            if (endermanSprite != null)
            {
                UnityEngine.Object.Destroy(endermanSprite);
                endermanSprite = null;
            }
        }

        private void UnregisterFarmerBreadStock()
        {
            if (originalFarmerSupplier == null)
                return;

            if (farmerSupplierIndex >= 0 && farmerSupplierIndex < StoreClientList.sellClientList.Count &&
                StoreClientList.sellClientList[farmerSupplierIndex] == (Func<StoreClient>)CreateFarmerWithMinecraftBread)
            {
                StoreClientList.sellClientList[farmerSupplierIndex] = originalFarmerSupplier;
            }

            originalFarmerSupplier = null;
            farmerSupplierIndex = -1;
        }

        private void SetMinecraftSpriteFiltering(LoadedMod loaded)
        {
            if (loaded == null || loaded.FolderPath == null || loaded.Sprites == null)
                return;

            string folder = loaded.FolderPath.TrimEnd('/', '\\');
            if (!folder.EndsWith(ModId, StringComparison.OrdinalIgnoreCase))
                return;

            foreach (KeyValuePair<string, UnityEngine.Sprite> entry in loaded.Sprites)
            {
                if (entry.Value != null && entry.Value.texture != null)
                {
                    entry.Value.texture.filterMode = UnityEngine.FilterMode.Point;
                    entry.Value.texture.anisoLevel = 0;
                }
            }

            UnityEngine.Texture2D villagerTexture;
            if (loaded.Textures.TryGetValue("villager_farmer", out villagerTexture) && villagerTexture != null)
            {
                villagerTexture.filterMode = UnityEngine.FilterMode.Point;
                villagerTexture.anisoLevel = 0;
                villagerSprite = UnityEngine.Sprite.Create(
                    villagerTexture,
                    new UnityEngine.Rect(0f, 0f, villagerTexture.width, villagerTexture.height),
                    new UnityEngine.Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    UnityEngine.SpriteMeshType.FullRect);
                RegisterVillagerSprite();
            }
            UnityEngine.Texture2D endermanTexture;
            if (loaded.Textures.TryGetValue("enderman_customer", out endermanTexture) && endermanTexture != null)
            {
                endermanTexture.filterMode = UnityEngine.FilterMode.Point;
                endermanTexture.anisoLevel = 0;
                endermanSprite = UnityEngine.Sprite.Create(
                    endermanTexture,
                    new UnityEngine.Rect(0f, 0f, endermanTexture.width, endermanTexture.height),
                    new UnityEngine.Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    UnityEngine.SpriteMeshType.FullRect);
                RegisterEndermanSprite();
            }
            log.Log("Applied crisp pixel filtering to Minecraft item sprites.");
        }

        private void RegisterItems(ModItemDirectory directory)
        {
            if (!LoadDefinitions())
                return;

            int registered = 0;
            foreach (ItemDefinition def in items)
            {
                ItemDefinition captured = def;
                if (directory.Add(captured.Id, () => CreateItem(captured)))
                    registered++;
            }
            log.Log("Registered " + registered + " Minecraft 1.20.1 items from Content/items.csv.");
        }

        private bool LoadDefinitions()
        {
            if (items.Count > 0)
                return true;

            byte[] bytes = ModHelper.ReadModFileBytes(ModId, "Content/items.csv");
            if (bytes == null || bytes.Length == 0)
            {
                log.Error("Could not read Content/items.csv.");
                return false;
            }

            string csv = Encoding.UTF8.GetString(bytes);
            string[] lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] fields = line.Split(',');
                int width;
                int height;
                string tier = fields.Length >= 5 ? fields[2].Trim() : "";
                if ((fields.Length != 5 && fields.Length != 6) || !PriceTiers.ContainsKey(tier) ||
                    !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) || width < 1 || width > 2 ||
                    !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) || height < 1 || height > 2)
                {
                    log.Warning("Skipping invalid item row " + (i + 1) + ".");
                    continue;
                }

                string[] types = fields.Length == 6
                    ? Array.ConvertAll(fields[5].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries), type => type.Trim())
                    : Array.Empty<string>();
                items.Add(new ItemDefinition(fields[0].Trim(), fields[1].Trim(), PriceTiers[tier], width, height, types));
            }
            return items.Count > 0;
        }

        private GameItem CreateItem(ItemDefinition def)
        {
            string nameKey = "item_" + def.Id + "_name";
            string descKey = "item_" + def.Id + "_desc";
            GameItem item = new GameItemElement(def.Id, "Items/itematlas", "unknown");
            item.name = ModHelper.GetLocalized(ModId, nameKey);
            item.shortDescription = ModHelper.GetLocalized(ModId, descKey);
            item.flavorText = item.shortDescription;
            item.SetValue(def.Price);
            item.SetSpriteAndShapeFromMod(ModId, def.Sprite);
            foreach (string type in def.Types)
                item.SetGameItemType(type);
            int stackLimit = GetStackLimit(def.Id);
            item.mayStackItemFunc = (left, right) => stackLimit > 1 && left != null && right != null &&
                left.identifier == right.identifier && MatchesFootprint(left, def) && MatchesFootprint(right, def);
            item.maxSlotAmountItemFunc = (target, inventory, slot) => stackLimit;

            item.SetShape(new GridShapeBuilder(def.Width, def.Height).SetDataFill(1));
            if (def.Id == "mc_bread")
            {
                // The base game routes calorie-tagged food into its existing animal-feed systems.
                item.SetGameItemType("FOOD");
                FoodItemHelper.InitFoodItem(item, 250, isCanned: false);
                AudioHelper.InitFoodAudio(item);
            }
            else if (def.Id == "mc_crafting_table")
            {
                item.EnableTag(CraftingHelper.CraftingTags.Table);
                CraftingRecipeManager craftingRecipes = new CraftingRecipeManager();
                item.recipeManager = craftingRecipes;
                BuildDirectCraftingMode(item, AddCraftingRecipes(craftingRecipes));
            }
            else if (def.Id == "mc_storage_chest")
            {
                InitializeMinecraftStorageChest(item);
            }
            else if (def.Id == "mc_furnace")
            {
                InitializeMinecraftFurnace(item);
            }
            item.onOneSecondItemFunc += (placed, _, __) => SyncFootprint(placed, def);
            return item;
        }

        private static int GetStackLimit(string itemId)
        {
            return itemId == "mc_stone_pickaxe" || itemId == "mc_iron_pickaxe" ||
                   itemId == "mc_diamond_pickaxe" || itemId == "mc_furnace" ? 1 :
                   itemId == "mc_music_disc_13" ? 16 : 64;
        }

        private static bool MatchesFootprint(GameItem item, ItemDefinition definition) =>
            IsSolidFootprint(item.shape, definition) && IsSolidFootprint(item.modifiedShape, definition);

        private static bool IsSolidFootprint(GridShape shape, ItemDefinition definition)
        {
            if (shape.width != definition.Width || shape.height != definition.Height)
                return false;
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++)
                    if (shape.GetLocal(x, y) != 1)
                        return false;
            return true;
        }

        private void SyncFootprint(GameItem item, ItemDefinition definition)
        {
            if (MatchesFootprint(item, definition) ||
                item.parentInventory == null || ItemMouseDragHandler.current?.IsDraggingItem == true ||
                !GeneralHelper.IsItemEffectivelyOwned(item))
                return;

            GridShapeBuilder expanded = new GridShapeBuilder(definition.Width, definition.Height).SetDataFill(1)
                .SetTransform(item.modifiedShape);
            GameInventory parent = item.parentInventory;
            if (parent is GameSlotInventory && !parent.IsInsertLocked())
            {
                ApplyFootprint(item, definition, expanded);
                return;
            }
            if (parent.CheckInventorySlotStillValid(item, expanded, item.modifiedState))
            {
                ApplyFootprint(item, definition, expanded);
                return;
            }
            if (!(parent is GameGridInventory grid))
                return;
            // Search only this container. Never detach a stack that has no safe destination.
            for (int y = 0; y <= grid.inventoryShape.height - expanded.globalHeight; y++)
            {
                for (int x = 0; x <= grid.inventoryShape.width - expanded.globalWidth; x++)
                {
                    expanded.SetPosition(x, y);
                    if (!grid.CheckInventorySlotStillValid(item, expanded, item.modifiedState))
                        continue;
                    ApplyFootprint(item, definition, expanded);
                    return;
                }
            }
        }

        private void ApplyFootprint(GameItem item, ItemDefinition definition, GridShapeBuilder placement)
        {
            int oldWidth = item.modifiedShape.globalWidth;
            int oldHeight = item.modifiedShape.globalHeight;
            item.modifiedShape = placement;
            item.SetShape(new GridShapeBuilder(definition.Width, definition.Height).SetDataFill(1));
            OverlayHandler.current.UpdateInventoryStateUI();
            log.Log($"Updated footprint for {item.identifier} x{item.unitCount}: {oldWidth}x{oldHeight} -> " +
                $"{item.modifiedShape.globalWidth}x{item.modifiedShape.globalHeight}.");
        }

        private List<RecipeEntry> AddCraftingRecipes(RecipeManager manager)
        {
            byte[] bytes = ModHelper.ReadModFileBytes(ModId, "Content/recipes.csv");
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Could not read Content/recipes.csv.");

            string[] lines = Encoding.UTF8.GetString(bytes).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var entries = new List<RecipeEntry>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] fields = line.Split(',');
                int outputAmount;
                if (fields.Length < 6 || (fields.Length - 4) % 2 != 0 ||
                    !int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out outputAmount) || outputAmount < 1)
                {
                    log.Warning("Skipping invalid recipe row " + (i + 1) + ".");
                    continue;
                }

                string outputId = fields[0].Trim();
                if (!DirectoryMaster.Has<GameItem>(outputId))
                {
                    log.Warning("Skipping recipe row " + (i + 1) + ": output item '" + outputId + "' is not registered.");
                    continue;
                }

                CraftingRecipe recipe = new CraftingRecipe(ModHelper.GetLocalized(ModId, fields[2].Trim()));
                bool valid = true;
                for (int field = 4; field < fields.Length; field += 2)
                {
                    string materialId = fields[field].Trim();
                    int materialAmount;
                    if (!DirectoryMaster.Has<GameItem>(materialId) ||
                        !int.TryParse(fields[field + 1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out materialAmount) ||
                        materialAmount < 1)
                    {
                        valid = false;
                        break;
                    }
                    recipe.AddMaterialIdentifier(materialId, materialAmount);
                }

                if (!valid)
                {
                    log.Warning("Skipping recipe row " + (i + 1) + ": a material is missing or its amount is invalid.");
                    continue;
                }

                recipe.SetDescription(ModHelper.GetLocalized(ModId, fields[3].Trim()));
                recipe.SetCraftingConstantFunc(outputId, outputAmount);
                manager.AddRecipe(recipe);
                entries.Add(new RecipeEntry(recipe, outputId, outputAmount));
            }

            if (entries.Count == 0)
                throw new InvalidOperationException("Content/recipes.csv did not contain any valid recipes.");
            log.Log("Registered " + entries.Count + " crafting recipes from Content/recipes.csv.");
            return entries;
        }

        private void BuildDirectCraftingMode(GameItem table, List<RecipeEntry> recipes)
        {
            const int pageSize = 6;
            const int listWidth = 128;
            const int detailsWidth = 224;
            var directWindow = new PixelWindow(true, ModHelper.GetLocalized(ModId, "direct_crafting_title"), WindowStyle.Machinery);
            ScrollableRichTextElement details = new ScrollableRichTextElement(constWidth: true);
            details.ResizePixels(detailsWidth, 212);
            var craftButton = new MachineryButton(ModHelper.GetLocalized(ModId, "direct_crafting_make"), width: detailsWidth, height: 22);
            var recipeList = new MachineryStack(gap: 5).SetMainAlign(MachineryStack.Align.Start);
            var buttons = new List<MachineryButton>();
            var detailsColumn = new MachineryStack(gap: 7).SetMainAlign(MachineryStack.Align.Start)
                .Add(details).Add(craftButton);
            LoadedMod loaded = ModLoader.Mods.Find(mod => mod.Manifest.ID == ModId);
            int pageCount = (recipes.Count + pageSize - 1) / pageSize;
            int page = 0;
            int selected = 0;
            string feedback = null;
            MachineryButton previous = null, next = null;
            MachineryLabel pageLabel = null;

            void RefreshDetails()
            {
                RecipeEntry entry = recipes[selected];
                var materials = GetCraftingMaterials();
                bool canCraft = PlanCraftingMaterials(entry.Recipe, materials) != null;
                var text = new RichTextBuilder();
                text.AddLine(ModHelper.GetLocalized(ModId, "direct_crafting_output"), bold: true);
                text.AddLine(ModHelper.GetLocalized(ModId, "direct_crafting_output_count", GetItemName(entry.OutputId), entry.OutputAmount),
                    color: RenderHandler.ColorPalette.LightTurquoise);
                text.AddLine(" ");
                text.AddLine(ModHelper.GetLocalized(ModId, "direct_crafting_materials"), bold: true);
                foreach (CraftingFilter ingredient in entry.Recipe.requiredMaterials)
                {
                    long owned = 0;
                    foreach (GameItem item in materials)
                        if (ingredient.Filter(item))
                            owned += item.MaxNumRemove();
                    text.AddLine(ModHelper.GetLocalized(ModId, "direct_crafting_material_count", ingredient.displayName, owned, ingredient.amount),
                        color: owned < ingredient.amount ? RenderHandler.ColorPalette.LightRed : RenderHandler.ColorPalette.LightGreen);
                    if (owned < ingredient.amount)
                        text.AddLine(ModHelper.GetLocalized(ModId, "direct_crafting_material_missing", ingredient.amount - owned),
                            color: RenderHandler.ColorPalette.LightRed);
                }
                if (!string.IsNullOrEmpty(feedback))
                    text.AddLine(" ").AddLine(feedback, color: RenderHandler.ColorPalette.LightRed);
                details.SetRichText(text);
                if (canCraft) craftButton.Enable(); else craftButton.Disable();
            }

            void RefreshList()
            {
                for (int row = 0; row < buttons.Count; row++)
                {
                    int index = page * pageSize + row;
                    var button = buttons[row];
                    button.SetHidden(index >= recipes.Count);
                    if (index >= recipes.Count)
                        continue;
                    RecipeEntry entry = recipes[index];
                    string caption = GetItemName(entry.OutputId);
                    ItemDefinition definition = items.Find(item => item.Id == entry.OutputId);
                    UnityEngine.Sprite sprite = null;
                    if (definition != null && loaded != null)
                        loaded.Sprites.TryGetValue(definition.Sprite, out sprite);
                    button.SetIcon(sprite).SetText(index == selected ? "<color=#B9E7B4>" + caption + "</color>" : caption);
                }
                if (pageLabel != null)
                {
                    pageLabel.SetText($"{page + 1} / {pageCount}");
                    if (page > 0) previous.Enable(); else previous.Disable();
                    if (page + 1 < pageCount) next.Enable(); else next.Disable();
                }
                directWindow.Validate();
            }

            void Select(int index)
            {
                if (index < 0 || index >= recipes.Count)
                    return;
                selected = index;
                page = index / pageSize;
                feedback = null;
                RefreshList();
                RefreshDetails();
                details.SetScroll(0f);
            }

            for (int row = 0; row < Math.Min(pageSize, recipes.Count); row++)
            {
                int capturedRow = row;
                var button = new MachineryButton("", () => Select(page * pageSize + capturedRow), listWidth, 36);
                buttons.Add(button);
                recipeList.Add(button);
            }
            if (pageCount > 1)
            {
                previous = new MachineryButton("<", () => Select((page - 1) * pageSize), 22, 18).SetFillWidth(false);
                next = new MachineryButton(">", () => Select((page + 1) * pageSize), 22, 18).SetFillWidth(false);
                pageLabel = new MachineryLabel().SetWidth(44);
                recipeList.Add(new MachineryStack(vertical: false, gap: 8).Add(previous).Add(pageLabel).Add(next));
            }

            craftButton.SetCallback(() =>
            {
                CraftingRecipe selectedRecipe = recipes[selected].Recipe;
                feedback = null;
                List<GameItem> materials = GetCraftingMaterials();
                Dictionary<GameItem, int> consumption = PlanCraftingMaterials(selectedRecipe, materials);
                if (consumption == null)
                {
                    RefreshDetails();
                    return;
                }

                List<GameItem> outputs = selectedRecipe.CraftWithoutMaterials();
                GameItem product = outputs.Count == 1 ? outputs[0] : null;
                SlotMarker outputSlot = product == null ? null : FindWholeStackSlot(GetCraftingDestinations(), product);
                int outputAmount = product?.unitCount ?? 0;
                if (outputSlot == null || outputSlot.TryAcceptOnce(outputAmount) != outputAmount)
                {
                    outputs.ForEach(item => item.Destroy());
                    feedback = ModHelper.GetLocalized(ModId, "direct_crafting_no_room");
                    RefreshDetails();
                    return;
                }

                if (outputSlot.targetItem != null && product.unitCount == 0)
                    product.Destroy();
                foreach (KeyValuePair<GameItem, int> use in consumption)
                    ConsumeAmount(use.Key, use.Value);
                OverlayHandler.current.UpdateInventoryStateUI();
                RefreshDetails();
            });

            directWindow.Attach(new MachineryPanelRow(
                new MachineryPanel(ModHelper.GetLocalized(ModId, "direct_crafting_recipes"), recipeList, "recipes"),
                new MachineryPanel(ModHelper.GetLocalized(ModId, "direct_crafting_details"), detailsColumn, "details")));
            table.SetContentWindow(directWindow);
            directWindow.openCallback = () => { feedback = null; RefreshDetails(); };
            directWindow.AddInventoryStateChangeAction(RefreshDetails);
            Select(0);
        }

        private string GetItemName(string itemId) => items.Exists(item => item.Id == itemId)
            ? ModHelper.GetLocalized(ModId, "item_" + itemId + "_name")
            : itemId;

        private static Dictionary<GameItem, int> PlanCraftingMaterials(CraftingRecipe recipe, List<GameItem> materials)
        {
            var consumption = new Dictionary<GameItem, int>();
            foreach (CraftingFilter ingredient in recipe.requiredMaterials)
            {
                int remaining = ingredient.amount;
                foreach (GameItem item in materials)
                {
                    if (!ingredient.Filter(item))
                        continue;
                    consumption.TryGetValue(item, out int reserved);
                    int count = Math.Min(remaining, item.MaxNumRemove() - reserved);
                    if (count <= 0)
                        continue;
                    consumption[item] = reserved + count;
                    remaining -= count;
                    if (remaining == 0)
                        break;
                }
                if (remaining != 0)
                    return null;
            }
            return consumption;
        }

        private static SlotMarker FindWholeStackSlot(IEnumerable<GraphNodeStorage> destinations, GameItem item)
        {
            foreach (GraphNodeStorage destination in destinations)
            {
                SlotMarker slot = destination?.TryFindOneValidInventorySlot(item);
                if (slot != null && slot.numTransfer >= item.unitCount)
                    return slot;
                // The game's merge result does not always fill in SlotMarker.inventory.
                // Keep searching this grid when the first stack cannot take the whole batch.
                if (!(destination is GameGridInventory grid))
                    continue;
                foreach (GameItem stack in grid.childItems)
                {
                    SlotMarker merge = stack.GetStackSlot(item);
                    if (merge != null && merge.numTransfer >= item.unitCount && grid.MayHaveValidInventorySlot(item))
                        return merge;
                }
                GridShapeBuilder shape = new GridShapeBuilder(item.modifiedShape);
                for (int y = 0; y < grid.inventoryShape.height; y++)
                {
                    for (int x = 0; x < grid.inventoryShape.width; x++)
                    {
                        shape.SetPosition(x, y);
                        SlotMarker empty = grid.TryInventorySlot(item, item.unitCount, shape);
                        if (empty.numTransfer >= item.unitCount)
                            return empty;
                    }
                }
            }
            return null;
        }

        private List<GameItem> GetCraftingMaterials()
        {
            EmporiumEntry player = EmporiumEntry.Instance;
            List<GameItem> materials = new List<GameItem>();
            if (player == null)
                return materials;

            AddCraftingMaterials(player.invElement, materials);
            AddCraftingMaterials(player.frontInvinvElement, materials);
            AddCraftingMaterials(player.backInvinvElement, materials);
            AddCraftingMaterials(player.afterhourInventory, materials);
            GameItem chest = materials.Find(item => item != null && item.identifier == "mc_storage_chest");
            AddCraftingMaterials(FindChestStorage(chest?.contentWindow), materials);
            return materials;
        }

        private static void AddCraftingMaterials(GameInventory inventory, List<GameItem> materials)
        {
            if (inventory == null)
                return;
            foreach (GameItem item in GraphUtils.FindAllChildrenType<GameItem>(inventory))
            {
                if (item != null && GeneralHelper.IsItemOwned(item) && item.MayRemove() && !materials.Contains(item))
                    materials.Add(item);
            }
        }

        private List<GraphNodeStorage> GetCraftingDestinations()
        {
            EmporiumEntry player = EmporiumEntry.Instance;
            List<GraphNodeStorage> destinations = new List<GraphNodeStorage>();
            if (player == null)
                return destinations;
            destinations.Add(player.invElement);
            destinations.Add(player.afterhourInventory);
            GameItem chest = GetCraftingMaterials().Find(item => item != null && item.identifier == "mc_storage_chest");
            destinations.Add(FindChestStorage(chest?.contentWindow));
            return destinations;
        }

        private bool LoadSmeltingRecipes()
        {
            if (smeltingRecipes.Count > 0)
                return true;

            byte[] bytes = ModHelper.ReadModFileBytes(ModId, "Content/smelting.csv");
            if (bytes == null || bytes.Length == 0)
            {
                log.Error("Could not read Content/smelting.csv.");
                return false;
            }

            string[] lines = Encoding.UTF8.GetString(bytes).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] fields = line.Split(',');
                if (fields.Length != 2 || !DirectoryMaster.Has<GameItem>(fields[0].Trim()) ||
                    !DirectoryMaster.Has<GameItem>(fields[1].Trim()))
                {
                    log.Warning("Skipping invalid smelting recipe row " + (i + 1) + ".");
                    continue;
                }
                smeltingRecipes.Add(new SmeltingDefinition(fields[0].Trim(), fields[1].Trim()));
            }
            return smeltingRecipes.Count > 0;
        }

        private void InitializeMinecraftFurnace(GameItem furnace)
        {
            LoadSmeltingRecipes();
            furnace.onLoaded = loaded =>
            {
                loaded.shortDescription = ModHelper.GetLocalized(ModId, "item_mc_furnace_desc");
                loaded.flavorText = loaded.shortDescription;
            };
            GameSlotInventory oreSlot = new GameSlotInventory(2, 2);
            GameSlotInventory fuelSlot = new GameSlotInventory(2, 2);
            GameSlotInventory outputSlot = new GameSlotInventory(2, 2);
            oreSlot.mayInventoryAddItemFunc = (item, _) => FindSmeltingRecipe(item) != null && GeneralHelper.IsItemOwned(item);
            fuelSlot.mayInventoryAddItemFunc = (item, _) => IsFurnaceFuel(item) && GeneralHelper.IsItemOwned(item);
            outputSlot.mayInventoryAddItemFunc = (_, _) => false;

            PixelWindow window = new PixelWindow(true, ModHelper.GetLocalized(ModId, "furnace_title"), WindowStyle.Machinery);
            ScrollableRichTextElement status = new ScrollableRichTextElement(constWidth: true);
            int statusWidth = UISettings.current.fontWidth * 48;
            status.ResizePixels(statusWidth, UISettings.current.fontHeight * 7);
            MachineryProgressBar progressBar = new MachineryProgressBar(width: statusWidth);
            void Refresh()
            {
                if (!window.IsVisible())
                    return;
                FurnaceClock clock = ReadFurnaceClock(furnace);
                SmeltingDefinition recipe = FindSmeltingRecipe(oreSlot.childItem);
                string currentStatus = GetFurnaceStatus(recipe, oreSlot.childItem, fuelSlot.childItem, outputSlot.childItem, clock);
                int progress = currentStatus == "furnace_working" && clock.InputId == recipe?.InputId ? clock.Progress : 0;
                progressBar.SetValue((float)progress / FurnaceSecondsPerOre);
                RichTextBuilder details = new RichTextBuilder();
                details.AddLine(string.Format(ModHelper.GetLocalized(ModId, currentStatus), progress, FurnaceSecondsPerOre));
                details.AddLine(string.Format(ModHelper.GetLocalized(ModId, "furnace_burn_time"), clock.BurnTime));
                details.AddLine(ModHelper.GetLocalized(ModId, "furnace_hint"));
                status.SetRichText(details);
            }

            furnace.onOneSecondItemFunc = (item, parent, _) =>
            {
                // The game invokes this once per real second and once per simulated second during sleep.
                // Do not collapse same-frame callbacks: the sleep simulation intentionally advances time.
                if (parent == null || !GeneralHelper.IsItemEffectivelyOwned(item))
                    return;
                TickFurnace(item, oreSlot, fuelSlot, outputSlot);
                Refresh();
            };
            MachineryPanelRow slots = new MachineryPanelRow(
                MachineHelper.Panel(ModHelper.GetLocalized(ModId, "furnace_ore_slot"), oreSlot, "ore"),
                MachineHelper.Panel(ModHelper.GetLocalized(ModId, "furnace_fuel_slot"), fuelSlot, "fuel"),
                MachineHelper.Panel(ModHelper.GetLocalized(ModId, "furnace_output_slot"), outputSlot, "output"));
            window.Attach(MachineryStack.Vertical(slots, progressBar, status));
            window.openCallback = Refresh;
            window.AddInventoryStateChangeAction(Refresh);
            furnace.SetContentWindow(window);
        }

        private string GetFurnaceStatus(SmeltingDefinition recipe, GameItem ore, GameItem fuel, GameItem output, FurnaceClock clock)
        {
            if (recipe == null || !CanConsume(ore))
                return "furnace_need_ore";
            if (!CanSmeltInto(recipe, output))
                return "furnace_output_full";
            if (clock.BurnTime == 0 && (!IsFurnaceFuel(fuel) || !CanConsume(fuel)))
                return "furnace_need_fuel";
            return "furnace_working";
        }

        private void TickFurnace(GameItem furnace, GameSlotInventory oreSlot, GameSlotInventory fuelSlot, GameSlotInventory outputSlot)
        {
            GameItem ore = oreSlot.childItem;
            GameItem fuel = fuelSlot.childItem;
            SmeltingDefinition recipe = FindSmeltingRecipe(ore);
            FurnaceClock clock = ReadFurnaceClock(furnace);
            int burnTime = clock.BurnTime;
            string inputId = recipe?.InputId ?? "";
            int progress = clock.InputId == inputId ? clock.Progress : 0;
            bool canSmelt = recipe != null && CanConsume(ore) && CanSmeltInto(recipe, outputSlot.childItem);
            bool inventoryChanged = false;

            if (burnTime == 0 && canSmelt && IsFurnaceFuel(fuel) && CanConsume(fuel))
            {
                ConsumeAmount(fuel, 1);
                burnTime = FurnaceSecondsPerFuel;
                inventoryChanged = true;
            }

            if (burnTime > 0)
            {
                burnTime--;
                progress = canSmelt ? progress + 1 : 0;
                if (progress == FurnaceSecondsPerOre)
                {
                    progress = 0;
                    if (TryAddSmeltedItem(recipe, outputSlot))
                    {
                        ConsumeAmount(ore, 1);
                        inventoryChanged = true;
                    }
                }
            }
            else
            {
                progress = Math.Max(0, progress - 2);
            }

            if (clock.BurnTime != burnTime || clock.Progress != progress || clock.InputId != inputId)
            {
                // Save all three values with the furnace and its existing slot contents.
                furnace.ModifyTag(FurnaceClockTag, tag => tag.Enable().SetInt(burnTime).SetLong(progress).SetString(inputId), skipValidate: true);
            }
            if (inventoryChanged)
                OverlayHandler.current.UpdateInventoryStateUI();
        }

        private bool TryAddSmeltedItem(SmeltingDefinition recipe, GameSlotInventory output)
        {
            if (output.childItem != null)
            {
                output.childItem.SetAmount(output.childItem.unitCount + 1);
                return true;
            }
            GameItem product = ModHelper.SpawnItem(recipe.OutputId);
            if (product != null && output.UncheckedAccept(product))
                return true;
            product?.Destroy();
            log.Warning("Could not place furnace output '" + recipe.OutputId + "'. Ore was kept.");
            return false;
        }

        private SmeltingDefinition FindSmeltingRecipe(GameItem ore) =>
            ore == null ? null : smeltingRecipes.Find(recipe => recipe.InputId == ore.identifier);

        private static bool IsFurnaceFuel(GameItem item) =>
            item != null && (item.identifier == "mc_coal" || item.identifier == "mc_charcoal");

        private static bool CanConsume(GameItem item) =>
            item != null && GeneralHelper.IsItemOwned(item) && item.MayRemove() && item.MaxNumRemove() > 0;

        private static bool CanSmeltInto(SmeltingDefinition recipe, GameItem output) =>
            output == null || (output.identifier == recipe.OutputId && output.MaxNumAdd() > 0);

        private static FurnaceClock ReadFurnaceClock(GameItem furnace)
        {
            if (furnace.state.dict.TryGetValue(FurnaceClockTag, out TagState clock))
            {
                return new FurnaceClock(Math.Max(0, Math.Min(FurnaceSecondsPerFuel, clock.GetInt())),
                    (int)Math.Max(0L, Math.Min(FurnaceSecondsPerOre - 1L, clock.GetLong())), clock.GetString());
            }
            if (furnace.state.dict.TryGetValue(LegacyFurnaceHeatTag, out TagState oldHeat))
                return new FurnaceClock(Math.Max(0, Math.Min(7, oldHeat.GetInt())) * FurnaceSecondsPerOre, 0, "");
            return new FurnaceClock(0, 0, "");
        }

        private static void ConsumeAmount(GameItem item, int amount)
        {
            if (item.unitCount == amount)
                item.Destroy();
            else
                item.SetAmount(item.unitCount - amount);
        }

        private void InitializeMinecraftStorageChest(GameItem item)
        {
            PixelWindow window = new PixelWindow(true, null, WindowStyle.Machinery)
                .SetTitle(ModHelper.GetLocalized(ModId, "item_mc_storage_chest_name"));
            bool upgraded = string.Equals(item.customText, "tier2", StringComparison.Ordinal);
            GameGridInventory storage = new GameGridInventory(9, upgraded ? 6 : 3);
            MachineryStack layout = new MachineryStack(true, 6);
            MachineryLabel capacityLabel = new MachineryLabel(ModHelper.GetLocalized(ModId, upgraded ? "storage_chest_tier2" : "storage_chest_tier1"));
            layout.Attach(capacityLabel);
            layout.Attach(storage);
            layout.Attach(new MachineryLabel(ModHelper.GetLocalized(ModId, "stack_controls_hint")));
            window.Attach(layout);
            if (!upgraded)
            {
                window.AddHeaderButton(new MachineryButton(
                    ModHelper.GetLocalized(ModId, "storage_chest_upgrade_button"),
                    () => UpgradeStorageChest(item, storage, capacityLabel), 180, 16)
                    .SetDescription(ModHelper.GetLocalized(ModId, "storage_chest_upgrade_desc")));
            }
            item.SetContentWindow(window);
        }

        private void UpgradeStorageChest(GameItem chest, GameGridInventory storage, MachineryLabel capacityLabel)
        {
            const int cost = 500;
            PlayerStore store = PlayerStore.Instance;
            if (chest == null || storage == null || string.Equals(chest.customText, "tier2", StringComparison.Ordinal) || store == null)
                return;
            if (!store.HasCredit(cost))
            {
                log.Log("Storage chest upgrade declined: not enough credits.");
                return;
            }
            store.PayMoney(cost, false);
            chest.customText = "tier2";
            storage.SetShape(9, 6);
            capacityLabel.SetText(ModHelper.GetLocalized(ModId, "storage_chest_tier2"));
            chest.CloseContentWindow(false);
            log.Log("Upgraded Minecraft storage chest from 27 to 54 slots.");
        }

        private void GrantStarterChest()
        {
            if (ModHelper.GetModDataBool(ModId, StarterChestGrantedKey, false))
                return;
            ModHelper.Delay(ModId, 1.5f, () =>
            {
                if (ModHelper.GetModDataBool(ModId, StarterChestGrantedKey, false))
                    return;
                if (GiveStarterChest())
                {
                    ModHelper.SetModDataBool(ModId, StarterChestGrantedKey, true);
                    log.Log("Granted the free 27-slot Minecraft storage chest.");
                }
            });
        }

        private void SyncLoadedFootprints()
        {
            EmporiumEntry player = EmporiumEntry.Instance;
            if (player == null)
                return;
            foreach (GameItem item in player.GetAllItems())
            {
                if (item?.identifier == null || !item.identifier.StartsWith("mc_", StringComparison.Ordinal))
                    continue;
                ItemDefinition definition = items.Find(candidate => candidate.Id == item.identifier);
                if (definition != null)
                    SyncFootprint(item, definition);
            }
        }

        private static GameGridInventory FindChestStorage(GraphNodeStorage node)
        {
            if (node == null || node.children == null)
                return null;
            foreach (GraphNodeStorage child in node.children)
            {
                GameGridInventory grid = child as GameGridInventory;
                if (grid != null)
                    return grid;
                grid = FindChestStorage(child);
                if (grid != null)
                    return grid;
            }
            return null;
        }

        private bool GiveStarterChest()
        {
            const string itemId = "mc_storage_chest";
            EmporiumEntry emporium = EmporiumEntry.Instance;
            if (emporium?.invElement == null)
            {
                log.Warning("Could not spawn '" + itemId + "' because the main inventory is unavailable.");
                return false;
            }

            GameItem item = ModHelper.SpawnItem(itemId);
            if (item == null)
            {
                log.Warning("Could not spawn '" + itemId + "'. Check that item registration completed.");
                return false;
            }

            SlotMarker slot = FindWholeStackSlot(new[] { emporium.invElement }, item);
            if (slot == null || slot.TryAcceptOnce(1) != 1)
            {
                item.Destroy();
                log.Warning("Could not spawn '" + itemId + "' because the main inventory has no room.");
                return false;
            }

            if (slot.targetItem != null && item.unitCount == 0)
                item.Destroy();
            log.Log("Spawned '" + itemId + "' directly into the main inventory.");
            return true;
        }

        private void BuildMinePanel(CustomUIBuilder builder)
        {
            builder.AddLabel(ModHelper.GetLocalized(ModId, "afterhour_mines_desc"));
            CustomUIElement status = builder.AddLabel("").Last;
            CustomUIElement result = builder.AddWarning("").Last.SetVisible(false);
            float nextRefresh = 0f;
            builder.AddButton(ModHelper.GetLocalized(ModId, "afterhour_mines_mine"), () =>
            {
                string message = SearchMine();
                result.SetText(message ?? "").SetVisible(message != null);
                nextRefresh = 0f;
                refreshMinePanel?.Invoke();
            });
            CustomUIElement search = builder.Last;
            int lastTier = -1;
            int lastRemaining = -1;

            refreshMinePanel = () =>
            {
                float now = UnityEngine.Time.unscaledTime;
                if (!builder.Handle.IsAlive || now < nextRefresh)
                    return;
                nextRefresh = now + 0.25f;
                int tier = GetBestPickaxeTier();
                int limit = GetMineSearchLimit(tier);
                int remaining = Math.Max(0, limit - GetMineSearchesUsed(StoreStation.GetDayCounter()));
                if (tier == lastTier && remaining == lastRemaining)
                    return;
                lastTier = tier;
                lastRemaining = remaining;
                status.SetText(tier == 0
                    ? ModHelper.GetLocalized(ModId, "afterhour_mines_locked")
                    : ModHelper.GetLocalized(ModId, "afterhour_mines_remaining", GetItemName(GetPickaxeId(tier)), remaining, limit));
                search.SetInteractable(remaining > 0);
            };
            refreshMinePanel();
        }

        private static int GetMineSearchLimit(int tier) => tier == 0 ? 0 : tier + 1;

        private static int GetMineSearchesUsed(int day) =>
            ModHelper.GetAfterhourCounter(MineId, "search_day", -1) == day
                ? ModHelper.GetAfterhourCounter(MineId, "searches_used", 0)
                : 0;

        private string SearchMine()
        {
            int tier = GetBestPickaxeTier();
            if (tier <= 0)
                return ModHelper.GetLocalized(ModId, "afterhour_mines_locked");

            int day = StoreStation.GetDayCounter();
            int searchesUsed = GetMineSearchesUsed(day);
            if (searchesUsed >= GetMineSearchLimit(tier))
                return ModHelper.GetLocalized(ModId, "afterhour_mines_empty");

            string table = tier == 1 ? TableStone : tier == 2 ? TableIron : TableDiamond;
            GameItem reward = ModHelper.SpawnFromLootTable(table);
            if (reward == null)
            {
                log.Warning("Mine loot table returned no item.");
                return ModHelper.GetLocalized(ModId, "afterhour_mines_unavailable");
            }

            string rewardId = reward.identifier;
            GameGridInventory bag = EmporiumEntry.Instance?.afterhourInventory;
            int amount = reward.unitCount;
            SlotMarker destination = FindWholeStackSlot(new[] { bag }, reward);
            if (amount <= 0 || destination == null || reward.MaxNumRemove() < amount || destination.TryAcceptOnce(amount) != amount)
            {
                reward.Destroy();
                return ModHelper.GetLocalized(ModId, "afterhour_mines_bag_full");
            }

            if (reward.unitCount == 0)
                reward.Destroy();
            ModHelper.SetAfterhourCounter(MineId, "search_day", day);
            ModHelper.SetAfterhourCounter(MineId, "searches_used", searchesUsed + 1);
            log.Log($"Mine sent '{rewardId}' to the afterhours bag (search {searchesUsed + 1}/{GetMineSearchLimit(tier)}).");
            return null;
        }

        private int GetBestPickaxeTier()
        {
            int best = 0;
            EmporiumEntry emporium = EmporiumEntry.Instance;
            if (emporium == null)
                return 0;

            // Once outside, the game moves the selected backpack to its own afterhours slot.
            foreach (GameItem item in emporium.GetAllAfterhourOwnedItems())
            {
                best = Math.Max(best, GetItemPickaxeTier(item));
                if (best == 3)
                    break;
            }
            return best;
        }

        private static string GetPickaxeId(int tier) =>
            tier == 3 ? "mc_diamond_pickaxe" : tier == 2 ? "mc_iron_pickaxe" : "mc_stone_pickaxe";

        private static int GetItemPickaxeTier(GameItem item)
        {
            if (item == null)
                return 0;
            if (item.identifier == "mc_diamond_pickaxe")
                return 3;
            if (item.identifier == "mc_iron_pickaxe")
                return 2;
            if (item.identifier == "mc_stone_pickaxe")
                return 1;
            return 0;
        }

        private sealed class ItemDefinition
        {
            public readonly string Id;
            public readonly string Sprite;
            public readonly int Price;
            public readonly int Width;
            public readonly int Height;
            public readonly string[] Types;

            public ItemDefinition(string id, string sprite, int price, int width, int height, string[] types)
            {
                Id = id;
                Sprite = sprite;
                Price = price;
                Width = width;
                Height = height;
                Types = types;
            }
        }

        private sealed class RecipeEntry
        {
            public readonly CraftingRecipe Recipe;
            public readonly string OutputId;
            public readonly int OutputAmount;

            public RecipeEntry(CraftingRecipe recipe, string outputId, int outputAmount)
            {
                Recipe = recipe;
                OutputId = outputId;
                OutputAmount = outputAmount;
            }
        }

        private readonly struct FurnaceClock
        {
            public readonly int BurnTime;
            public readonly int Progress;
            public readonly string InputId;

            public FurnaceClock(int burnTime, int progress, string inputId)
            {
                BurnTime = burnTime;
                Progress = progress;
                InputId = inputId;
            }
        }

        private sealed class SmeltingDefinition
        {
            public readonly string InputId;
            public readonly string OutputId;

            public SmeltingDefinition(string inputId, string outputId)
            {
                InputId = inputId;
                OutputId = outputId;
            }
        }
    }
}
