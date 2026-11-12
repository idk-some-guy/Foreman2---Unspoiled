local RESEARCH_KEYS = {
	'mining_drill_productivity_bonus', 'laboratory_speed_modifier', 'laboratory_productivity_bonus',
	'inserter_stack_size_bonus', 'bulk_inserter_capacity_bonus', 'belt_stack_size_bonus',
	'worker_robots_speed_modifier', 'worker_robots_storage_bonus', 'worker_robots_battery_modifier',
	'train_braking_force_bonus'
}

local function MarkTestResearch()
	local force = game.forces['player']
	local function Researched(name) pcall(function() force.technologies[name].researched = true end) end
	local function Level(name, level) pcall(function() force.technologies[name].level = level end) end
	Researched('mining-productivity-1')
	Level('mining-productivity-3', 5)
	Level('steel-plate-productivity', 3)
	Researched('inserter-capacity-bonus-2')
	Researched('transport-belt-capacity-1')
	Researched('worker-robots-speed-2')
	Researched('worker-robots-storage-1')
	Researched('braking-force-1')
	Researched('research-speed-1')
end

local function ReadForceResearch(force, research, recipeproductivity)
	for _, key in ipairs(RESEARCH_KEYS) do
		local ok, value = pcall(function() return force[key] end)
		if ok and type(value) == 'number' and (research[key] == nil or value > research[key]) then
			research[key] = value
		end
	end
	for _, recipe in pairs(force.recipes) do
		local ok, value = pcall(function() return recipe.productivity_bonus end)
		if ok and type(value) == 'number' and value > 0 and (recipeproductivity[recipe.name] == nil or value > recipeproductivity[recipe.name]) then
			recipeproductivity[recipe.name] = value
		end
	end
end

local function AddTechnology(technologies, tech)
	local ttech = {}
	ttech['name'] = tech.name
	ttech['enabled'] = tech.researched
	ttech['level'] = tech.level
	ttech['min_level'] = tech.prototype.level
	ttech['max_level'] = tech.prototype.max_level
	table.insert(technologies, ttech)
end

local function Export()
	local ok, testresearch = pcall(function() return settings.startup['foreman-savereader-test-research'].value end)
	if ok and testresearch and game.forces['player'] ~= nil then
		MarkTestResearch()
	end

	exporttable = {}
	local research = {}
	local recipeproductivity = {}
	exporttable['research'] = research
	exporttable['recipe_productivity'] = recipeproductivity
	exporttable['ticks_played'] = game.ticks_played
	exporttable['mods'] = {}
	exporttable['technologies'] = {}
	exporttable['recipes'] = {}


	for name, version in pairs(script.active_mods) do
		ntable = {}
		ntable['name'] = name
		ntable['version'] = version
		table.insert(exporttable['mods'], ntable)
	end

	if game.forces['player'] ~= nil then
		ReadForceResearch(game.forces['player'], research, recipeproductivity)
		for _, tech in pairs(game.forces['player'].technologies) do
			AddTechnology(exporttable['technologies'], tech)
		end
		for _, recipe in pairs(game.forces['player'].recipes) do
				trecipe = {}
				trecipe['name'] = recipe.name
				trecipe['enabled'] = recipe.enabled
				table.insert(exporttable['recipes'], trecipe)
		end
	else --couldnt find the 'player' force, so just read all forces and assume a complete amalgamation of all researched tech and recipes works.
		for _, force in pairs(game.forces) do
			ReadForceResearch(force, research, recipeproductivity)
			for _, tech in pairs(force.technologies) do
				AddTechnology(exporttable['technologies'], tech)
			end
			for _, recipe in pairs(force.recipes) do
				trecipe = {}
				trecipe['name'] = recipe.name
				trecipe['enabled'] = recipe.enabled
				table.insert(exporttable['recipes'], trecipe)
			end
		end
	end

	localised_print('<<<START-EXPORT-P0>>>')
	localised_print(helpers.table_to_json(exporttable))
	localised_print('<<<END-EXPORT-P0>>>')

end

script.on_nth_tick(1, 	
	function()

		Export()	

		ENDEXPORTANDJUSTDIE() -- just the most safe way of ensuring that we export once and quit. Basically... there is no ENDEXPORTANDJUSTDIE function. so lua will throw an expection and the run will end here.
	end
)