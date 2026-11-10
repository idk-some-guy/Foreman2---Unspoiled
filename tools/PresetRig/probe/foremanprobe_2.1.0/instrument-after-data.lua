local function plain(value, depth)
  depth = depth or 0
  local kind = type(value)
  if kind == "number" or kind == "string" or kind == "boolean" then
    return value
  end
  if kind ~= "table" then
    return nil
  end
  if depth > 5 then
    return "<deep>"
  end
  local count = 0
  for _ in pairs(value) do
    count = count + 1
  end
  local out = {}
  if count == #value then
    for index, item in ipairs(value) do
      out[index] = plain(item, depth + 1)
    end
  else
    for key, item in pairs(value) do
      out[tostring(key)] = plain(item, depth + 1)
    end
  end
  return out
end

local function sorted_keys(map)
  local keys = {}
  for key in pairs(map) do
    keys[#keys + 1] = key
  end
  table.sort(keys)
  return keys
end

local function signature(value)
  if type(value) ~= "table" then
    return tostring(value)
  end
  local parts = {}
  for _, key in ipairs(sorted_keys(value)) do
    parts[#parts + 1] = tostring(key) .. "=" .. signature(value[key])
  end
  return "{" .. table.concat(parts, ",") .. "}"
end

local payload = {}

local effect_groups = {}
local effect_total = 0
for type_name, prototypes in pairs(data.raw) do
  for name, prototype in pairs(prototypes) do
    if type(prototype) == "table" and prototype.effect_receiver ~= nil then
      effect_total = effect_total + 1
      local key = signature(prototype.effect_receiver)
      local group = effect_groups[key]
      if not group then
        group = { effect_receiver = plain(prototype.effect_receiver), count = 0, names = {} }
        effect_groups[key] = group
      end
      group.count = group.count + 1
      if #group.names < 30 then
        group.names[#group.names + 1] = type_name .. "/" .. name
      end
    end
  end
end
local groups = {}
for _, key in ipairs(sorted_keys(effect_groups)) do
  groups[#groups + 1] = effect_groups[key]
end
payload.effect_receiver = { entities_with_receiver = effect_total, groups = groups }

local towers = {}
for name, tower in pairs(data.raw["agricultural-tower"] or {}) do
  towers[name] = {
    module_slots = tower.module_slots,
    quality_affects_module_slots = tower.quality_affects_module_slots,
    allowed_effects = plain(tower.allowed_effects),
    allowed_module_categories = plain(tower.allowed_module_categories),
    effect_receiver = plain(tower.effect_receiver),
    energy_usage = tower.energy_usage,
    radius = tower.radius,
    input_inventory_size = tower.input_inventory_size,
  }
end
payload.agricultural_tower = towers

local qualities = {}
for name, quality in pairs(data.raw.quality or {}) do
  local fields = {}
  for key, value in pairs(quality) do
    if key ~= "icon" and key ~= "icons" and key ~= "icon_size" then
      fields[key] = plain(value)
    end
  end
  qualities[name] = fields
end
payload.quality = qualities

local recipe_total, explicit_true, explicit_false, has_category, has_categories = 0, 0, 0, 0, 0
local explicit_false_names = {}
for name, recipe in pairs(data.raw.recipe or {}) do
  recipe_total = recipe_total + 1
  if recipe.can_set_quality == true then
    explicit_true = explicit_true + 1
  elseif recipe.can_set_quality == false then
    explicit_false = explicit_false + 1
    explicit_false_names[#explicit_false_names + 1] = name
  end
  if recipe.category ~= nil then
    has_category = has_category + 1
  end
  if recipe.categories ~= nil then
    has_categories = has_categories + 1
  end
end
table.sort(explicit_false_names)
payload.recipe_fields = {
  recipes = recipe_total,
  can_set_quality_explicit_true = explicit_true,
  can_set_quality_explicit_false = explicit_false,
  can_set_quality_explicit_false_names = explicit_false_names,
  with_category = has_category,
  with_categories = has_categories,
}

local fuel_category, fuel_categories = 0, 0
local with_spoil_quality = {}
for section in pairs(defines.prototypes.item) do
  for name, item in pairs(data.raw[section] or {}) do
    if item.fuel_category ~= nil then
      fuel_category = fuel_category + 1
    end
    if item.fuel_categories ~= nil then
      fuel_categories = fuel_categories + 1
    end
    if item.spoil_quality_min ~= nil or item.spoil_quality_max ~= nil or (item.spoil_quality_change or 0) ~= 0 then
      with_spoil_quality[#with_spoil_quality + 1] = {
        name = name,
        spoil_quality_min = item.spoil_quality_min,
        spoil_quality_max = item.spoil_quality_max,
        spoil_quality_change = item.spoil_quality_change,
      }
    end
  end
end
payload.item_fields = {
  with_fuel_category = fuel_category,
  with_fuel_categories = fuel_categories,
  with_spoil_quality = with_spoil_quality,
}

data:extend({ { type = "mod-data", name = "foreman-probe-data", data = payload } })
