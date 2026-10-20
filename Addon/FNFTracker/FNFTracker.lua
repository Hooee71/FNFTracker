--[[
FNF Tracker addon

Records each arena match into FNFTrackerDB (account-wide SavedVariables) so
the FNF Tracker desktop app can import it. It is read-only: it never acts on
your behalf. It only listens to events and reads the scoreboard.

WoW writes SavedVariables to disk on /reload, logout or exit, so the app
picks up a night's games after you log off.

Midnight note: some scoreboard values are "secret" while a PvP match is
active. We never do math or comparisons on scoreboard values here. Anything
still secret is saved as nil, and the record is re-read a few times after the
match ends so the final copy has the real numbers. The app does all the math.
]]

local ADDON_NAME = ...
local VERSION = "0.1.0"
local TWO_V_TWO_BRACKET = 1 -- GetPersonalRatedInfo bracket index for 2v2

local current        -- the arena we're in right now
local lastRecord     -- most recent saved record (gets post-match rating on exit)
local wantRating     -- waiting to read 2v2 rating after leaving an arena

local function EnsureDB()
    FNFTrackerDB = FNFTrackerDB or {}
    FNFTrackerDB.version = 1
    FNFTrackerDB.matches = FNFTrackerDB.matches or {}
end

local function Print(msg)
    DEFAULT_CHAT_FRAME:AddMessage("|cffe6b450FNF|r " .. msg)
end

-- Returns v unless it's a secret value (Midnight), in which case nil.
local function Clean(v)
    if v == nil then return nil end
    if issecretvalue and issecretvalue(v) then return nil end
    return v
end

local function Try(fn, ...)
    if not fn then return nil end
    local ok, a, b, c, d, e, f, g = pcall(fn, ...)
    if ok then return a, b, c, d, e, f, g end
    return nil
end

local function InArena()
    local _, instanceType = GetInstanceInfo()
    return instanceType == "arena"
end

local function NewId()
    return date("%Y%m%d-%H%M%S") .. "-" .. math.random(1000, 9999)
end

local function StartMatch()
    local mapName, _, _, _, _, _, _, instanceID = GetInstanceInfo()
    local playerName = UnitName("player")
    local realm = Try(GetNormalizedRealmName) or ""
    current = {
        id = NewId(),
        startedAt = time(),
        map = mapName,
        mapId = instanceID,
        rated = Clean(Try(C_PvP.IsRatedArena)) and true or false,
        skirmish = Clean(Try(IsArenaSkirmish)) and true or false,
        recordedBy = playerName .. "-" .. realm,
        prep = {},
    }
end

-- Before the gates open: enemy specs. Backup in case the scoreboard hides them.
local function CaptureSpecs()
    if not current then StartMatch() end
    local n = Clean(Try(GetNumArenaOpponentSpecs)) or 0
    for i = 1, n do
        local specID = Clean(Try(GetArenaOpponentSpec, i))
        if specID then
            local _, specName, _, _, role, classFile = Try(GetSpecializationInfoByID, specID)
            current.prep[i] = {
                specId = specID,
                spec = Clean(specName),
                class = Clean(classFile),
                role = Clean(role),
            }
        end
    end
end

local function IsMe(scoreName)
    local me = UnitName("player")
    if not scoreName or not me then return false end
    return scoreName == me or scoreName:sub(1, #me + 1) == me .. "-"
end

-- Reads the full scoreboard into the record. Safe to call repeatedly; each call
-- replaces the previous read so the last (most complete) one wins.
local function CaptureScoreboard()
    if not current or not current.completed then return end
    local n = Clean(Try(GetNumBattlefieldScores)) or 0
    if n == 0 then return end

    local players, hidden = {}, 0
    for i = 1, n do
        local s = Try(C_PvP.GetScoreInfo, i)
        if s then
            local p = {
                name = Clean(s.name),
                class = Clean(s.classToken),
                spec = Clean(s.talentSpec),
                team = Clean(s.faction),
                damage = Clean(s.damageDone),
                healing = Clean(s.healingDone),
                rating = Clean(s.rating),
                ratingChange = Clean(s.ratingChange),
                mmrBefore = Clean(s.prematchMMR),
                mmrAfter = Clean(s.postmatchMMR),
            }
            p.isMe = IsMe(p.name) or nil
            if p.spec == nil or p.ratingChange == nil then hidden = hidden + 1 end
            players[#players + 1] = p
        end
    end

    local rec = current.record
    local isNew = rec == nil
    if isNew then
        EnsureDB()
        rec = { id = current.id }
        current.record = rec
        table.insert(FNFTrackerDB.matches, rec)
    end

    rec.addonVersion = VERSION
    rec.startedAt = current.startedAt
    rec.endedAt = current.endedAt
    rec.map = current.map
    rec.mapId = current.mapId
    rec.rated = current.rated
    rec.skirmish = current.skirmish
    rec.recordedBy = current.recordedBy
    rec.myTeam = Clean(Try(GetBattlefieldArenaFaction))
    rec.winner = current.winner or Clean(Try(C_PvP.GetActiveMatchWinner))
    rec.duration = current.duration or Clean(Try(C_PvP.GetActiveMatchDuration))
    rec.players = players
    rec.prep = current.prep
    rec.hiddenFields = hidden > 0 and hidden or nil
    lastRecord = rec

    if isNew then
        Print(string.format("recorded %s match (%d players). /fnf for history.", rec.map or "arena", #players))
    end
end

local function OnMatchComplete(winner, duration)
    if not current then StartMatch() end
    current.completed = true
    current.endedAt = time()
    current.winner = Clean(winner)
    current.duration = Clean(duration)
    current.rated = (Clean(Try(C_PvP.IsRatedArena)) and true) or current.rated
    Try(RequestBattlefieldScoreData)
    CaptureScoreboard()
    -- Re-read a few times: rating changes and un-hidden values arrive late.
    for _, delay in ipairs({ 1, 3, 6, 12 }) do
        C_Timer.After(delay, function()
            if current and current.completed then
                Try(RequestBattlefieldScoreData)
                CaptureScoreboard()
            end
        end)
    end
end

-- After leaving the arena: your 2v2 rating is now definitely the post-match number.
local function CaptureRatingAfter()
    if not wantRating or not lastRecord then return end
    local rating = Clean(Try(GetPersonalRatedInfo, TWO_V_TWO_BRACKET))
    if rating and rating > 0 then
        lastRecord.ratingAfter2v2 = rating
        wantRating = false
    end
end

local function OnZoneChange()
    if InArena() then
        if not current then StartMatch() end
    elseif current then
        -- 4 players = 2v2; the rating we read afterwards is the 2v2 bracket's.
        if current.record and current.rated and #(current.record.players or {}) == 4 then
            wantRating = true
            Try(RequestRatedInfo)
            C_Timer.After(3, CaptureRatingAfter)
        end
        current = nil
    end
end

local function ShowHistory()
    local matches = FNFTrackerDB and FNFTrackerDB.matches or {}
    Print(string.format("%d match(es) saved. Log out or /reload to write them for the app.", #matches))
    for i = math.max(1, #matches - 4), #matches do
        local m = matches[i]
        local me, enemies = nil, {}
        for _, p in ipairs(m.players or {}) do
            if p.isMe then me = p end
        end
        local myTeam = (me and me.team) or m.myTeam
        for _, p in ipairs(m.players or {}) do
            if myTeam ~= nil and p.team ~= nil and p.team ~= myTeam then
                enemies[#enemies + 1] = (p.spec or "?") .. " " .. (p.class or "?")
            end
        end
        local result = "?"
        if m.winner ~= nil and myTeam ~= nil then
            result = (m.winner == myTeam) and "|cff4cc38aW|r" or "|cffe5534bL|r"
        end
        Print(string.format("  %s %s %s vs %s", date("%a %H:%M", m.startedAt or 0), result,
            m.map or "", #enemies > 0 and table.concat(enemies, " + ") or "?"))
    end
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("ZONE_CHANGED_NEW_AREA")
frame:RegisterEvent("ARENA_PREP_OPPONENT_SPECIALIZATIONS")
frame:RegisterEvent("PVP_MATCH_COMPLETE")
frame:RegisterEvent("UPDATE_BATTLEFIELD_SCORE")
frame:RegisterEvent("PVP_RATED_STATS_UPDATE")

frame:SetScript("OnEvent", function(_, event, ...)
    if event == "ADDON_LOADED" then
        if ... == ADDON_NAME then EnsureDB() end
    elseif event == "PLAYER_ENTERING_WORLD" or event == "ZONE_CHANGED_NEW_AREA" then
        OnZoneChange()
    elseif event == "ARENA_PREP_OPPONENT_SPECIALIZATIONS" then
        CaptureSpecs()
    elseif event == "PVP_MATCH_COMPLETE" then
        if InArena() then OnMatchComplete(...) end
    elseif event == "UPDATE_BATTLEFIELD_SCORE" then
        CaptureScoreboard()
    elseif event == "PVP_RATED_STATS_UPDATE" then
        CaptureRatingAfter()
    end
end)

SLASH_FNFTRACKER1 = "/fnf"
SlashCmdList.FNFTRACKER = ShowHistory
