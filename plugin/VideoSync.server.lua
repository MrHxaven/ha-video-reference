--[[
	HA Video Sync — sends Heaven's Animator's frame to the HA Video Reference app (a small local server on this
	computer, started with "Start Video Reference.bat"), so a reference video follows the timeline while you scrub
	or play. A separate plugin with its own toolbar tab, not part of Heaven's Animator; works with the full edition
	and Lite (it follows _G.HeavensAnimator). The first Connect makes Studio ask to let it reach localhost: allow it.

	POST http://localhost:<port>/sync {frame, fps, playing, speed}: at most 30 times a second while something changes,
	and once a second as a heartbeat (the app then shows "Synced with Studio"). One request at a time; the newest
	state wins. The port and the on / off state are remembered (plugin settings).
]]

local RunService = game:GetService("RunService")
if not plugin or RunService:IsRunning() or not RunService:IsEdit() then
	return
end
local HttpService = game:GetService("HttpService")

local PORT_SETTING, ON_SETTING = "HAVideoSync_Port", "HAVideoSync_On"
local DEFAULT_PORT = 34900
local SEND_INTERVAL = 1 / 30
local HEARTBEAT = 1
local ICON = "rbxassetid://97131655361849" -- the drawn logo with a video badge (assets/icons/video_sync_*.png)

local port = tonumber(plugin:GetSetting(PORT_SETTING)) or DEFAULT_PORT
local on = plugin:GetSetting(ON_SETTING) == true

---------------------------------------------------------------------------------------------------
-- the window: port, Connect, status

local BG, PANEL, LINE = Color3.fromRGB(11, 13, 31), Color3.fromRGB(24, 28, 56), Color3.fromRGB(38, 43, 77)
local TEXT, MUTED, ACCENT = Color3.fromRGB(232, 234, 248), Color3.fromRGB(155, 161, 198), Color3.fromRGB(62, 197, 255)
local OK, WARN = Color3.fromRGB(74, 222, 128), Color3.fromRGB(251, 191, 36)

local toolbar = plugin:CreateToolbar("HA Video Reference")
local button = toolbar:CreateButton("Video Sync", "Sync a reference video (HA Video Reference app) to Heaven's Animator's timeline", ICON)
button.ClickableWhenViewportHidden = true

local widget = plugin:CreateDockWidgetPluginGui(
	"HAVideoSync",
	DockWidgetPluginGuiInfo.new(Enum.InitialDockState.Float, false, false, 320, 160, 280, 150)
)
widget.Title = "HA Video Sync"
widget.Name = "HAVideoSync"

local function new(class: string, props)
	local inst = Instance.new(class)
	for k, v in props do
		if k ~= "Parent" then
			(inst :: any)[k] = v
		end
	end
	inst.Parent = props.Parent
	return inst
end

local root = new("Frame", { Size = UDim2.fromScale(1, 1), BackgroundColor3 = BG, BorderSizePixel = 0, Parent = widget })
new("UIPadding", { PaddingLeft = UDim.new(0, 12), PaddingRight = UDim.new(0, 12), PaddingTop = UDim.new(0, 12), Parent = root })
new("UIListLayout", { Padding = UDim.new(0, 10), SortOrder = Enum.SortOrder.LayoutOrder, Parent = root })

local row = new("Frame", { Size = UDim2.new(1, 0, 0, 32), BackgroundTransparency = 1, LayoutOrder = 1, Parent = root })
new("TextLabel", {
	Size = UDim2.fromOffset(44, 32), BackgroundTransparency = 1, Text = "Port", TextColor3 = MUTED,
	TextSize = 16, Font = Enum.Font.GothamMedium, TextXAlignment = Enum.TextXAlignment.Left, Parent = row,
})
local portBox = new("TextBox", {
	Position = UDim2.fromOffset(48, 0), Size = UDim2.fromOffset(90, 32), BackgroundColor3 = PANEL, BorderSizePixel = 0,
	Text = tostring(port), TextColor3 = TEXT, TextSize = 16, Font = Enum.Font.GothamMedium, ClearTextOnFocus = false,
	Parent = row,
}) :: TextBox
new("UICorner", { CornerRadius = UDim.new(0, 4), Parent = portBox })
new("UIStroke", { Color = LINE, ApplyStrokeMode = Enum.ApplyStrokeMode.Border, Parent = portBox })
local connect = new("TextButton", {
	AnchorPoint = Vector2.new(1, 0), Position = UDim2.new(1, 0, 0, 0), Size = UDim2.fromOffset(120, 32),
	BackgroundColor3 = ACCENT, BorderSizePixel = 0, TextColor3 = Color3.fromRGB(4, 18, 38), TextSize = 16,
	Font = Enum.Font.GothamBold, Text = "Connect", Parent = row,
}) :: TextButton
new("UICorner", { CornerRadius = UDim.new(0, 4), Parent = connect })

local status = new("TextLabel", {
	Size = UDim2.new(1, 0, 0, 60), BackgroundTransparency = 1, LayoutOrder = 2, TextColor3 = MUTED, TextSize = 15,
	Font = Enum.Font.Gotham, TextWrapped = true, TextXAlignment = Enum.TextXAlignment.Left,
	TextYAlignment = Enum.TextYAlignment.Top, Text = "", Parent = root,
}) :: TextLabel

local shown = ""
local function setStatus(text: string, color: Color3?)
	if text ~= shown then
		shown = text
		status.Text = text
	end
	status.TextColor3 = color or MUTED
end

local function refreshButton()
	connect.Text = if on then "Disconnect" else "Connect"
	connect.BackgroundColor3 = if on then PANEL else ACCENT
	connect.TextColor3 = if on then TEXT else Color3.fromRGB(4, 18, 38)
	if not on then
		setStatus("Start the app (Start Video Reference), open a video in it, then press Connect.")
	end
end

---------------------------------------------------------------------------------------------------
-- sync

local function base(): string
	return "http://localhost:" .. tostring(port)
end

-- Heaven's Animator's state now, nil without an open animation
local function snapshot()
	local api = _G.HeavensAnimator
	local editor = if type(api) == "table" then api.editor else nil
	local doc = if type(editor) == "table" then editor.doc else nil
	if type(doc) ~= "table" then
		return nil
	end
	local speed = 1
	local pb = editor.playback
	if type(pb) == "table" and type(pb.speed) == "function" then
		local ok, s = pcall(pb.speed, pb)
		if ok and type(s) == "number" and s > 0 then
			speed = s
		end
	end
	return { frame = tonumber(editor.frame) or 0, fps = tonumber(doc.fps) or 60, playing = editor.playing == true, speed = speed }
end

local inFlight, pending = false, nil
local function send(state)
	pending = state
	if inFlight then
		return
	end
	inFlight = true
	task.spawn(function()
		while pending and on do
			local s = pending
			pending = nil
			local ok, result = pcall(function()
				return HttpService:RequestAsync({
					Url = base() .. "/sync",
					Method = "POST",
					Headers = { ["Content-Type"] = "application/json" },
					Body = HttpService:JSONEncode(s),
				})
			end)
			if ok and result.Success then
				setStatus(string.format("Synced · frame %d at %g fps%s", math.floor(s.frame + 0.5), s.fps, if s.playing then " · playing" else ""), OK)
			elseif ok then
				setStatus(string.format("The app answered %d on port %d: is that the HA Video Reference app?", result.StatusCode, port), WARN)
			else
				local why = tostring(result)
				if string.find(why, "ConnectFail", 1, true) or string.find(why, "refused", 1, true) then
					setStatus(string.format("Can't reach the app on port %d. Start it (Start Video Reference) and keep its window open.", port), WARN)
				else
					setStatus("Couldn't sync: " .. why .. " (allow localhost when Studio asks)", WARN)
				end
			end
		end
		inFlight = false
	end)
end

local lastSent, lastTime = nil, 0
local heartbeat = RunService.Heartbeat:Connect(function()
	if not on then
		return
	end
	local now = os.clock()
	if now - lastTime < SEND_INTERVAL then
		return
	end
	local s = snapshot()
	if s == nil then
		setStatus("Connected. Open an animation in Heaven's Animator to sync.", MUTED)
		return
	end
	local changed = lastSent == nil
		or s.frame ~= lastSent.frame
		or s.playing ~= lastSent.playing
		or s.fps ~= lastSent.fps
		or s.speed ~= lastSent.speed
	if changed or now - lastTime >= HEARTBEAT then
		lastSent, lastTime = s, now
		send(s)
	end
end)

local function setOn(value: boolean)
	on = value
	plugin:SetSetting(ON_SETTING, on)
	lastSent = nil
	refreshButton()
	if on then
		setStatus("Connecting…")
	end
end

portBox.FocusLost:Connect(function()
	local n = tonumber(portBox.Text)
	if n and n >= 1024 and n <= 65535 and n == math.floor(n) then
		port = n
		plugin:SetSetting(PORT_SETTING, port)
		lastSent = nil
	end
	portBox.Text = tostring(port)
end)
connect.Activated:Connect(function()
	setOn(not on)
end)
button.Click:Connect(function()
	widget.Enabled = not widget.Enabled
end)
widget:GetPropertyChangedSignal("Enabled"):Connect(function()
	button:SetActive(widget.Enabled)
end)
button:SetActive(widget.Enabled)

refreshButton()

plugin.Unloading:Connect(function()
	heartbeat:Disconnect()
	on = false
end)
