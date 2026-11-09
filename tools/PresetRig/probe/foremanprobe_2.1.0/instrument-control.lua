local FINAL_TICK = 3080

local function plain(value, depth)
  depth = depth or 0
  local kind = type(value)
  if kind == "number" then
    if value ~= value or value == math.huge or value == -math.huge then
      return tostring(value)
    end
    return value
  end
  if kind == "string" or kind == "boolean" then
    return value
  end
  if kind == "userdata" then
    local ok, name = pcall(function() return value.name end)
    if ok and name ~= nil then
      return tostring(name)
    end
    return tostring(value)
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

local function read(object, key)
  local ok, value = pcall(function() return object[key] end)
  if not ok then
    return { present = false, error = tostring(value) }
  end
  return { present = value ~= nil, value = plain(value), lua_type = type(value) }
end

local function call(object, method, ...)
  local arguments = { ... }
  local ok, value = pcall(function() return object[method](table.unpack(arguments)) end)
  if not ok then
    return { present = false, error = tostring(value) }
  end
  return { present = true, value = plain(value), lua_type = type(value) }
end

local function name_list(value)
  local out = {}
  if type(value) ~= "table" then
    return out
  end
  for key, item in pairs(value) do
    if type(key) == "number" then
      out[#out + 1] = tostring(plain(item))
    else
      out[#out + 1] = tostring(key)
    end
  end
  table.sort(out)
  return out
end

local result = {}
local behaviors = {}
local powered = {}

local function section(name, fn)
  local ok, value = pcall(fn)
  if ok then
    result[name] = value
  else
    result[name] = { error = tostring(value) }
  end
end

local function data_channel()
  local ok, value = pcall(function() return prototypes.mod_data["foreman-probe-data"].data end)
  if ok and value ~= nil then
    return value
  end
  return nil
end

local function surface()
  return game.surfaces[1]
end

local function spot(name, x, y)
  local position = surface().find_non_colliding_position(name, { x, y }, 60, 1)
  return position or { x, y }
end

local function keep_powered(entity)
  powered[#powered + 1] = entity
end

local function inventory_contents(entity)
  local out = {}
  for index = 1, 16 do
    local ok, inventory = pcall(function() return entity.get_inventory(index) end)
    if ok and inventory ~= nil then
      local items = {}
      for _, item in pairs(inventory.get_contents()) do
        items[#items + 1] = { name = item.name, quality = item.quality, count = item.count }
      end
      if #items > 0 then
        out[tostring(index)] = items
      end
    end
  end
  return out
end

local function recipe_categories(recipe)
  return name_list(recipe.categories)
end

local function shape_of(recipe)
  local item_in, fluid_in, item_out, fluid_out = 0, 0, 0, 0
  for _, ingredient in ipairs(recipe.ingredients) do
    if ingredient.type == "fluid" then
      fluid_in = fluid_in + 1
    else
      item_in = item_in + 1
    end
  end
  for _, product in ipairs(recipe.products) do
    if product.type == "fluid" then
      fluid_out = fluid_out + 1
    else
      item_out = item_out + 1
    end
  end
  return { item_ingredients = item_in, fluid_ingredients = fluid_in, item_products = item_out, fluid_products = fluid_out }
end

local function furnace_categories()
  local categories = {}
  for _, name in ipairs(sorted_keys(prototypes.entity)) do
    local entity = prototypes.entity[name]
    if entity.type == "furnace" then
      for category in pairs(entity.crafting_categories or {}) do
        categories[category] = categories[category] or {}
        table.insert(categories[category], name)
      end
    end
  end
  return categories
end

local function can_set_quality()
  local trues, falses, other = 0, 0, 0
  local false_recipes = {}
  local by_category = {}
  local by_shape = {}
  local furnaces = furnace_categories()
  local furnace_reachable = {}
  for _, name in ipairs(sorted_keys(prototypes.recipe)) do
    local recipe = prototypes.recipe[name]
    local value = recipe.can_set_quality
    local shape = shape_of(recipe)
    local shape_key = signature(shape)
    by_shape[shape_key] = by_shape[shape_key] or { shape = shape, can_set_quality_true = 0, can_set_quality_false = 0 }
    if value == true then
      trues = trues + 1
      by_shape[shape_key].can_set_quality_true = by_shape[shape_key].can_set_quality_true + 1
    elseif value == false then
      falses = falses + 1
      by_shape[shape_key].can_set_quality_false = by_shape[shape_key].can_set_quality_false + 1
      local categories = recipe_categories(recipe)
      false_recipes[#false_recipes + 1] = { name = name, categories = categories, shape = shape }
      local key = table.concat(categories, "+")
      by_category[key] = (by_category[key] or 0) + 1
      for _, category in ipairs(categories) do
        if furnaces[category] then
          furnace_reachable[#furnace_reachable + 1] = { recipe = name, category = category, furnaces = furnaces[category] }
        end
      end
    else
      other = other + 1
    end
  end
  local shapes = {}
  for _, key in ipairs(sorted_keys(by_shape)) do
    shapes[#shapes + 1] = by_shape[key]
  end
  return {
    true_count = trues,
    false_count = falses,
    other_count = other,
    false_by_category_list = by_category,
    by_recipe_shape = shapes,
    false_recipes = false_recipes,
    furnace_can_craft_a_locked_recipe = furnace_reachable,
    data_stage = (data_channel() or {}).recipe_fields,
  }
end

local function setup_quality_lock_craft()
  local state = { inserted = {} }
  local cases = {
    { key = "locked_uncommon", ore = "foreman-probe-locked-ore", quality = "uncommon" },
    { key = "locked_normal", ore = "foreman-probe-locked-ore", quality = "normal" },
    { key = "settable_uncommon", ore = "foreman-probe-open-ore", quality = "uncommon" },
    { key = "vanilla_uncommon", ore = "iron-ore", quality = "uncommon" },
  }
  for index, case in ipairs(cases) do
    local furnace = surface().create_entity({ name = "stone-furnace", position = spot("stone-furnace", 10 * index, 10), force = "player" })
    furnace.get_fuel_inventory().insert({ name = "coal", count = 20 })
    state.inserted[case.key] = furnace.get_inventory(defines.inventory.crafter_input).insert({ name = case.ore, count = 20, quality = case.quality })
    state[case.key] = furnace
  end
  return state
end

local function collect_quality_lock_craft(state)
  local out = { ore_inserted = state.inserted, test_recipes = {}, furnaces = {} }
  for _, variant in ipairs({ "locked", "open" }) do
    out.test_recipes[variant] = { can_set_quality = prototypes.recipe["foreman-probe-" .. variant .. "-smelting"].can_set_quality }
  end
  for _, key in ipairs({ "locked_uncommon", "locked_normal", "settable_uncommon", "vanilla_uncommon" }) do
    out.furnaces[key] = inventory_contents(state[key])
  end
  return out
end

local function product_amounts()
  local shared = {}
  local independent_groups = {}
  local independent_total = 0
  local fractions = {}
  for _, name in ipairs(sorted_keys(prototypes.recipe)) do
    local recipe = prototypes.recipe[name]
    for index, product in ipairs(recipe.products) do
      local fraction = product.extra_count_fraction
      if fraction ~= nil and fraction ~= 0 then
        fractions[#fractions + 1] = {
          recipe = name,
          index = index,
          product = plain(product),
          at_default = call(recipe, "get_product_amount", index),
          at_productivity_0 = call(recipe, "get_product_amount", index, 0),
          at_productivity_1 = call(recipe, "get_product_amount", index, 1),
        }
      end
      local range = product.shared_probability
      local is_shared = range ~= nil and (range.min ~= 0 or range.max ~= 1)
      local is_independent = product.independent_probability ~= nil and product.independent_probability ~= 1
      if is_shared or is_independent then
        local amounts = {
          recipe = name,
          index = index,
          product = plain(product),
          at_default = call(recipe, "get_product_amount", index),
          at_productivity_0 = call(recipe, "get_product_amount", index, 0),
          at_productivity_0_5 = call(recipe, "get_product_amount", index, 0.5),
          at_productivity_1 = call(recipe, "get_product_amount", index, 1),
        }
        if is_shared then
          shared[#shared + 1] = amounts
        else
          independent_total = independent_total + 1
          local stripped = plain(product)
          stripped.name = nil
          local key = signature(stripped)
          local group = independent_groups[key]
          if not group then
            group = { example = amounts, count = 0 }
            independent_groups[key] = group
          end
          group.count = group.count + 1
        end
      end
    end
  end
  local groups = {}
  for _, key in ipairs(sorted_keys(independent_groups)) do
    groups[#groups + 1] = independent_groups[key]
  end
  local capped = {}
  for index = 1, math.min(#groups, 60) do
    capped[index] = groups[index]
  end
  return {
    shared_probability_products = shared,
    independent_probability_products = independent_total,
    independent_distinct_shapes = #groups,
    independent_by_shape_first_60 = capped,
    extra_count_fraction_products = fractions,
  }
end

local function module_effects()
  local out = {}
  for _, name in ipairs(sorted_keys(prototypes.item)) do
    local item = prototypes.item[name]
    if item.type == "module" then
      local entry = {
        category = read(item, "category"),
        module_effects_field = read(item, "module_effects"),
        multipliers = {},
        by_quality = {},
      }
      for _, key in ipairs({ "consumption", "speed", "productivity", "pollution", "quality" }) do
        entry.multipliers[key] = read(item, key .. "_quality_multiplier")
      end
      for _, quality_name in ipairs(sorted_keys(prototypes.quality)) do
        entry.by_quality[quality_name] = call(item, "get_module_effects", quality_name)
      end
      out[name] = entry
    end
  end
  local per_quality = {}
  for _, quality_name in ipairs(sorted_keys(prototypes.quality)) do
    local quality = prototypes.quality[quality_name]
    local entry = {}
    for _, key in ipairs({ "consumption", "speed", "productivity", "pollution", "quality" }) do
      entry[key] = read(quality, "module_" .. key .. "_multiplier")
    end
    per_quality[quality_name] = entry
  end
  return { modules = out, quality_multipliers = per_quality }
end

local function roll_chances()
  local out = { qualities = {}, rolls = {} }
  for _, quality_name in ipairs(sorted_keys(prototypes.quality)) do
    local quality = prototypes.quality[quality_name]
    out.qualities[quality_name] = {
      level = quality.level,
      next = read(quality, "next"),
      next_probability = read(quality, "next_probability"),
      chain_probability = read(quality, "chain_probability"),
      previous_probability = read(quality, "previous_probability"),
      previous_chain_probability = read(quality, "previous_chain_probability"),
    }
  end
  for _, from in ipairs({ "normal", "uncommon" }) do
    for _, effect in ipairs({ 0.1, 0.4, 1.5, 12 }) do
      if from == "normal" or effect == 0.1 or effect == 0.4 then
        out.rolls[from .. "@" .. tostring(effect)] = call(prototypes.quality[from], "get_roll_chances", effect)
      end
    end
  end
  return out
end

local function effect_limits()
  local groups = {}
  local total, exposed = 0, 0
  local negative_quality_low = {}
  for _, name in ipairs(sorted_keys(prototypes.entity)) do
    local entity = prototypes.entity[name]
    local ok, receiver = pcall(function() return entity.effect_receiver end)
    if ok and receiver ~= nil then
      total = total + 1
      local summary = plain(receiver)
      local limits = {}
      if type(summary) == "table" then
        for _, key in ipairs({ "consumption", "speed", "productivity", "pollution", "quality" }) do
          local limit = summary[key .. "_limits"]
          if limit ~= nil then
            limits[key] = limit
            if key == "quality" and type(limit) == "table" and (limit.low or limit[1] or 0) < 0 then
              negative_quality_low[#negative_quality_low + 1] = name
            end
          end
        end
      end
      if next(limits) ~= nil then
        exposed = exposed + 1
      end
      local key = signature(summary)
      local group = groups[key]
      if not group then
        group = { receiver = summary, count = 0, names = {} }
        groups[key] = group
      end
      group.count = group.count + 1
      if #group.names < 30 then
        group.names[#group.names + 1] = entity.type .. "/" .. name
      end
    end
  end
  local list = {}
  for _, key in ipairs(sorted_keys(groups)) do
    list[#list + 1] = groups[key]
  end
  local channel = data_channel()
  return {
    runtime_entities_with_receiver = total,
    runtime_entities_exposing_limits = exposed,
    runtime_groups = list,
    runtime_negative_quality_low = negative_quality_low,
    data_stage = channel and channel.effect_receiver or nil,
  }
end

local function channel_keys(channel)
  local keys = {}
  if channel then
    for key in pairs(channel) do
      keys[#keys + 1] = key
    end
    table.sort(keys)
  end
  return keys
end

local function setup_agricultural_tower()
  local state = { towers = {} }
  local base = surface()
  local variants = {
    { label = "no_modules", modules = nil, x = 40 },
    { label = "productivity_modules", modules = { name = "productivity-module-3", count = 4 }, x = 60 },
    { label = "quality_modules", modules = { name = "quality-module-3", count = 4 }, x = 80 },
    { label = "quality_effect_one", modules = { name = "foreman-probe-quality-module", count = 1 }, x = 100 },
  }
  for _, variant in ipairs(variants) do
    local entry = { label = variant.label, plants = {} }
    local center = { x = variant.x, y = 40 }
    local tiles = {}
    for dx = -6, 6 do
      for dy = -6, 6 do
        tiles[#tiles + 1] = { name = "natural-yumako-soil", position = { center.x + dx, center.y + dy } }
      end
    end
    for _, other in ipairs(base.find_entities_filtered({ position = center, radius = 20, type = { "tree", "plant", "simple-entity", "cliff", "fish" } })) do
      other.destroy()
    end
    entry.tiles = pcall(function() base.set_tiles(tiles) end)
    local ok_tower, tower = pcall(function()
      return base.create_entity({ name = "foreman-probe-tower", position = center, force = "player" })
    end)
    entry.tower_created = ok_tower and tower ~= nil
    if ok_tower and tower then
      keep_powered(tower)
      entry.tower = tower
      if variant.modules then
        local ok_insert, inserted = pcall(function() return tower.get_module_inventory().insert(variant.modules) end)
        entry.modules_inserted = ok_insert and inserted or tostring(inserted)
      end
      entry.effects = read(tower, "effects")
      for index = 1, 2 do
        local angle = index * 3.14159265359
        local ok_plant, plant = pcall(function()
          return base.create_entity({ name = "yumako-tree", position = { center.x + 2.5 * math.cos(angle), center.y + 2.5 * math.sin(angle) }, force = "neutral" })
        end)
        if ok_plant and plant then
          pcall(function() plant.tick_grown = game.tick end)
          entry.plants[#entry.plants + 1] = plant
        else
          entry.plant_error = tostring(plant)
        end
      end
    else
      entry.tower_error = tostring(tower)
    end
    state.towers[#state.towers + 1] = entry
  end
  return state
end

local function agricultural_tower_prototype()
  local channel = data_channel()
  local entity = prototypes.entity["agricultural-tower"]
  local runtime = {
    module_inventory_size = read(entity, "module_inventory_size"),
    allowed_effects = read(entity, "allowed_effects"),
    allowed_module_categories = read(entity, "allowed_module_categories"),
    effect_receiver = read(entity, "effect_receiver"),
    quality_affects_module_slots = read(entity, "quality_affects_module_slots"),
  }
  local qualities = {}
  local absent_quality_keys = {}
  for _, quality_name in ipairs(sorted_keys(prototypes.quality)) do
    local quality = prototypes.quality[quality_name]
    local entry = {}
    for _, key in ipairs({ "mining_drill_module_slots_bonus", "crafting_machine_module_slots_bonus", "beacon_module_slots_bonus", "lab_module_slots_bonus", "agricultural_tower_module_slots_bonus", "module_slots_bonus" }) do
      local value = read(quality, key)
      if value.present then
        entry[key] = value.value
      else
        absent_quality_keys[key] = true
      end
    end
    qualities[quality_name] = entry
  end
  local test_entity = prototypes.entity["foreman-probe-tower"]
  return {
    runtime = runtime,
    runtime_per_quality = qualities,
    quality_keys_absent = name_list(absent_quality_keys),
    test_entity_present = test_entity ~= nil,
    test_entity_module_inventory_size = test_entity and read(test_entity, "module_inventory_size") or nil,
    data_stage = channel and channel.agricultural_tower or nil,
    data_stage_quality = channel and channel.quality or nil,
  }
end

local function collect_agricultural_tower(state)
  local out = {}
  local plant = prototypes.entity["yumako-tree"]
  out[1] = { plant_products = plain(plant.mineable_properties.products), plant_minable = plant.mineable_properties.minable }
  for _, entry in ipairs(state.towers) do
    local summary = {
      label = entry.label,
      test_entity = "foreman-probe-tower (a copy of the tower with four module slots)",
      tower_created = entry.tower_created,
      modules_inserted = entry.modules_inserted,
      plants_created = #entry.plants,
      plant_error = entry.plant_error,
      tower_error = entry.tower_error,
      tiles_set = entry.tiles,
      effects_before_run = entry.effects,
    }
    if entry.tower and entry.tower.valid then
      summary.tower_contents = inventory_contents(entry.tower)
      summary.effects_after_run = read(entry.tower, "effects")
      summary.output_inventory = inventory_contents(entry.tower)
    end
    local standing = 0
    for _, plant in ipairs(entry.plants) do
      if plant.valid then
        standing = standing + 1
      end
    end
    summary.plants_still_standing = standing
    out[#out + 1] = summary
  end
  return out
end

local function runtime_getters()
  local out = {}
  local coal = prototypes.item["coal"]
  out.item_fuel_categories = { read = read(coal, "fuel_categories"), fuel_category = read(coal, "fuel_category") }
  local sample_recipe = prototypes.recipe["iron-gear-wheel"]
  out.recipe_categories = { read = read(sample_recipe, "categories"), category = read(sample_recipe, "category"), additional_categories = read(sample_recipe, "additional_categories") }
  local pump = prototypes.entity["offshore-pump"]
  out.offshore_pump = {
    get_pumping_speed = call(pump, "get_pumping_speed"),
    pumping_speed = read(pump, "pumping_speed"),
  }
  local engine = prototypes.entity["steam-engine"]
  out.steam_engine = {
    get_fluid_usage_per_tick = call(engine, "get_fluid_usage_per_tick"),
    fluid_usage_per_tick = read(engine, "fluid_usage_per_tick"),
    get_max_power_output = call(engine, "get_max_power_output"),
    max_power_output = read(engine, "max_power_output"),
  }
  local fluidboxes = {}
  for _, name in ipairs({ "steam-engine", "boiler" }) do
    local entity = prototypes.entity[name]
    local boxes = {}
    local ok, list = pcall(function() return entity.fluidbox_prototypes end)
    if ok and list then
      for index, box in ipairs(list) do
        boxes[#boxes + 1] = {
          index = index,
          minimum_temperature = read(box, "minimum_temperature"),
          maximum_temperature = read(box, "maximum_temperature"),
        }
      end
    end
    fluidboxes[name] = boxes
  end
  out.fluidbox_temperatures = fluidboxes
  local input_temperatures = {}
  for _, name in ipairs({ "steam-engine", "boiler", "steam-turbine" }) do
    local entity = prototypes.entity[name]
    input_temperatures[name] = {
      minimum_input_temperature = read(entity, "minimum_input_temperature"),
      maximum_input_temperature = read(entity, "maximum_input_temperature"),
    }
  end
  out.entity_input_temperatures = input_temperatures
  local fluid_sources = {}
  local read_errors = {}
  for _, name in ipairs(sorted_keys(prototypes.entity)) do
    local entity = prototypes.entity[name]
    local attempt = read(entity, "fluid_energy_source_prototype")
    if attempt.error then
      local slot = read_errors[attempt.error] or { count = 0, first_entity = name }
      slot.count = slot.count + 1
      read_errors[attempt.error] = slot
    elseif attempt.present then
      local source = entity.fluid_energy_source_prototype
      fluid_sources[#fluid_sources + 1] = {
        entity = name,
        minimum_input_temperature = read(source, "minimum_input_temperature"),
        maximum_input_temperature = read(source, "maximum_input_temperature"),
      }
    end
  end
  out.fluid_energy_source = {
    none_found = #fluid_sources == 0,
    entities = fluid_sources,
    read_errors = read_errors,
  }
  local removed = { generators = {}, fluids = {} }
  for _, name in ipairs(sorted_keys(prototypes.entity)) do
    local entity = prototypes.entity[name]
    if entity.type == "generator" then
      removed.generators[name] = {
        maximum_temperature = read(entity, "maximum_temperature"),
        fluid_usage_per_tick = read(entity, "fluid_usage_per_tick"),
      }
    end
  end
  for _, name in ipairs({ "water", "steam", "crude-oil" }) do
    local fluid = prototypes.fluid[name]
    removed.fluids[name] = {
      max_temperature = read(fluid, "max_temperature"),
      gas_temperature = read(fluid, "gas_temperature"),
      heat_capacity = read(fluid, "heat_capacity"),
      default_temperature = read(fluid, "default_temperature"),
    }
  end
  out.removed_reads = removed
  return out
end

local function quality_fields()
  local fields = { "quality_min", "quality_max", "quality_change", "affected_by_quality", "spoil_weight", "always_fresh", "reset_freshness_on_craft" }
  local groups = {}
  local function note(recipe_name, role, entry)
    local seen = {}
    for _, field in ipairs(fields) do
      if entry[field] ~= nil then
        seen[field] = plain(entry[field])
      end
    end
    if next(seen) == nil then
      return
    end
    local key = role .. signature(seen)
    local group = groups[key]
    if not group then
      group = { role = role, fields = seen, count = 0, examples = {} }
      groups[key] = group
    end
    group.count = group.count + 1
    if #group.examples < 4 then
      group.examples[#group.examples + 1] = recipe_name .. ":" .. tostring(entry.name)
    end
  end
  for _, name in ipairs(sorted_keys(prototypes.recipe)) do
    local recipe = prototypes.recipe[name]
    for _, ingredient in ipairs(recipe.ingredients) do
      note(name, "ingredient", ingredient)
    end
    for _, product in ipairs(recipe.products) do
      note(name, "product", product)
    end
  end
  local list = {}
  for _, key in ipairs(sorted_keys(groups)) do
    list[#list + 1] = groups[key]
  end
  local spoil_items = {}
  for _, name in ipairs(sorted_keys(prototypes.item)) do
    local item = prototypes.item[name]
    local min = item.spoil_quality_min
    local max = item.spoil_quality_max
    local change = item.spoil_quality_change
    if min ~= nil or max ~= nil or (change or 0) ~= 0 then
      spoil_items[#spoil_items + 1] = { name = name, min = plain(min), max = plain(max), change = change }
    end
  end
  local channel = data_channel()
  return { recipe_fields = list, items_with_spoil_quality = spoil_items, data_stage = channel and channel.item_fields or nil }
end

local function count_entries(map)
  local count = 0
  for _ in pairs(map) do
    count = count + 1
  end
  return count
end

local function save_reader()
  local force = game.forces["player"]
  local technology = force and force.technologies["automation"]
  local recipe = force and force.recipes["inserter"]
  local active = {}
  for name, version in pairs(script.active_mods) do
    active[name] = version
  end
  return {
    active_mods = active,
    player_force_present = force ~= nil,
    technology_automation_researched = technology ~= nil and technology.researched,
    recipe_inserter_enabled = recipe ~= nil and recipe.enabled,
    technology_count = force ~= nil and count_entries(force.technologies) or 0,
    recipe_count = force ~= nil and count_entries(force.recipes) or 0,
  }
end

local spoil_variants = { "plain", "up", "down", "bounded", "bounded_up" }
local spoil_qualities = { "normal", "uncommon", "rare", "epic", "legendary" }

local function setup_spoil_quality()
  local chests = {}
  local row = 0
  for _, variant in ipairs(spoil_variants) do
    row = row + 1
    for column, quality_name in ipairs(spoil_qualities) do
      local chest = surface().create_entity({ name = "wooden-chest", position = spot("wooden-chest", 5 + column * 2, 30 + row * 2), force = "player" })
      chest.get_inventory(defines.inventory.chest).insert({ name = "foreman-probe-spoil-" .. variant, count = 1, quality = quality_name })
      chests[#chests + 1] = { variant = variant, quality = quality_name, chest = chest }
    end
  end
  return { chests = chests }
end

local function collect_spoil_quality(state)
  local mapping = {}
  for _, entry in ipairs(state.chests) do
    local result = {}
    for _, item in pairs(entry.chest.get_inventory(defines.inventory.chest).get_contents()) do
      result[#result + 1] = item.name .. "@" .. item.quality .. "x" .. tostring(item.count)
    end
    table.sort(result)
    mapping[entry.variant .. "@" .. entry.quality] = result
  end
  local items = {}
  for _, variant in ipairs(spoil_variants) do
    local prototype = prototypes.item["foreman-probe-spoil-" .. variant]
    items[variant] = {
      spoil_quality_min = read(prototype, "spoil_quality_min"),
      spoil_quality_max = read(prototype, "spoil_quality_max"),
      spoil_quality_change = read(prototype, "spoil_quality_change"),
    }
  end
  return { test_items = items, input_to_result_after_spoil = mapping }
end

local function add_behavior(name, setup, collect)
  behaviors[#behaviors + 1] = { name = name, setup = setup, collect = collect }
end

add_behavior("quality_lock_craft", setup_quality_lock_craft, collect_quality_lock_craft)
add_behavior("spoil_quality", setup_spoil_quality, collect_spoil_quality)
add_behavior("agricultural_tower_harvest", setup_agricultural_tower, collect_agricultural_tower)

local function static_sections()
  section("can_set_quality", can_set_quality)
  section("product_amounts", product_amounts)
  section("module_effects", module_effects)
  section("roll_chances", roll_chances)
  section("effect_limits", effect_limits)
  section("agricultural_tower", agricultural_tower_prototype)
  section("runtime_getters", runtime_getters)
  section("quality_fields", quality_fields)
  section("save_reader", save_reader)
  local channel = data_channel()
  result.mod_data_channel = { prototype_present = channel ~= nil, payload_keys = channel_keys(channel) }
end

local ticks = 0
local started = false

script.on_nth_tick(1, function()
  ticks = ticks + 1
  if not started then
    started = true
    surface().request_to_generate_chunks({ 0, 0 }, 4)
    surface().force_generate_chunk_requests()
    for _, behavior in ipairs(behaviors) do
      local ok, state = pcall(behavior.setup)
      behavior.state = state
      behavior.setup_ok = ok
    end
  end
  for index = #powered, 1, -1 do
    local entity = powered[index]
    if entity.valid then
      pcall(function() entity.energy = entity.electric_buffer_size end)
    end
  end
  if ticks == FINAL_TICK then
    static_sections()
    for _, behavior in ipairs(behaviors) do
      if behavior.setup_ok then
        section(behavior.name, function() return behavior.collect(behavior.state) end)
      else
        result[behavior.name] = { setup_error = tostring(behavior.state) }
      end
    end
    localised_print("<<<START-PROBE>>>")
    localised_print(helpers.table_to_json(result))
    localised_print("<<<END-PROBE>>>")
  end
end)
