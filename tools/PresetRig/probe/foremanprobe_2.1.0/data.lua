local spoil_variants = {
  plain = {},
  up = { spoil_quality_change = 1 },
  down = { spoil_quality_change = -1 },
  bounded = { spoil_quality_min = "uncommon", spoil_quality_max = "rare", spoil_quality_change = 0 },
  bounded_up = { spoil_quality_min = "uncommon", spoil_quality_max = "rare", spoil_quality_change = 1 },
}

for variant, fields in pairs(spoil_variants) do
  local item = {
    type = "item",
    name = "foreman-probe-spoil-" .. variant,
    icon = "__base__/graphics/icons/iron-plate.png",
    icon_size = 64,
    stack_size = 10,
    spoil_ticks = 1,
    spoil_result = "foreman-probe-result-" .. variant,
  }
  for key, value in pairs(fields) do
    item[key] = value
  end
  data:extend({
    item,
    {
      type = "item",
      name = "foreman-probe-result-" .. variant,
      icon = "__base__/graphics/icons/copper-plate.png",
      icon_size = 64,
      stack_size = 10,
    },
  })
end

local tower = data.raw["agricultural-tower"] and data.raw["agricultural-tower"]["agricultural-tower"]
if tower then
  local copy = table.deepcopy(tower)
  copy.name = "foreman-probe-tower"
  copy.minable = nil
  copy.next_upgrade = nil
  copy.fast_replaceable_group = nil
  copy.module_slots = 4
  copy.quality_affects_module_slots = true
  copy.allowed_effects = { "consumption", "speed", "productivity", "pollution", "quality" }
  data:extend({ copy })
end

local stone_furnace = data.raw.furnace and data.raw.furnace["stone-furnace"]
if stone_furnace then
  for _, variant in ipairs({ "locked", "open" }) do
    data:extend({
      {
        type = "item",
        name = "foreman-probe-" .. variant .. "-ore",
        icon = "__base__/graphics/icons/iron-ore.png",
        icon_size = 64,
        stack_size = 50,
      },
      {
        type = "recipe",
        name = "foreman-probe-" .. variant .. "-smelting",
        categories = { stone_furnace.crafting_categories[1] },
        enabled = true,
        energy_required = 1,
        can_set_quality = variant == "open",
        ingredients = { { type = "item", name = "foreman-probe-" .. variant .. "-ore", amount = 1 } },
        results = { { type = "item", name = "iron-plate", amount = 1 } },
      },
    })
  end
end

data:extend({
  {
    type = "module",
    name = "foreman-probe-quality-module",
    icon = "__base__/graphics/icons/iron-plate.png",
    icon_size = 64,
    stack_size = 50,
    category = "quality",
    tier = 1,
    effect = { quality = 1 },
  },
})

data:extend({
  {
    type = "recipe",
    name = "foreman-probe-fraction",
    categories = { "crafting" },
    main_product = "copper-plate",
    enabled = true,
    energy_required = 1,
    ingredients = { { type = "item", name = "iron-plate", amount = 1 } },
    results = {
      { type = "item", name = "copper-plate", amount = 1 },
      { type = "item", name = "iron-gear-wheel", amount = 0, extra_count_fraction = 0.5 },
    },
  },
})
