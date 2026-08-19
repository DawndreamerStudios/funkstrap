--!strict
--!native

-- FunkstrapSDK Helper Module v1.0.0
--[[
	Written by Minz (@Adrigamer278YT), Furo (@TheFuroYT) and Runa (@runameowhehe)
	
	Small guide:
	- Use FunkstrapSDK.UsesBloxstrap (enabled.png) to check for Bloxstrap usage and FunkstrapSDK.IsGameAllowed (%GAMEID%.png) to check for permissions
	- Call FunkstrapSDK.CheckWindowPermission() to update the window permission status
	- Call FunkstrapSDK.RequestWindowPermission() to prompt the user for window permission on their side, call allowed only once per game session (To avoid spam)
	
	// NOTES:
	Because of performance concerns, commands are processed every 250ms (1/4 seconds) unless StartWindow is called,
		which changes the process rate to the FPS set by the user (FPS check rate setting on Funkstrap).
		When you are done, call StopWindow which changes the process rate back to 250ms
	
	Wallpapers are downloaded from roblox's servers, so it's recommended you cache them first using FunkstrapSDK.CacheWallpaper()
	
	Despite caching being done on funkstrap side, it's recommended you send the full state, it'll get optimized in here
		Example: SetWindow sends just changes from last state update (new Y, new width, ...) but you should still pass the width, height, etc..
	
	RPC updates are ratelimited to 1 second (+ log read speed) on bloxstrap's side and requests may be dropped,
		in here they are queued so they may not appear on the console instantly (or at all if another request was overwritting it) while debugging to avoid them getting dropped
	
	// METHODS:
	- For window's title -> FunkstrapSDK.SetTitle() and FunkstrapSDK.ResetTitle()
	
	--- These methods below require user's permission
	
	- For window's sizing and positioning -> FunkstrapSDK.SetWindow() and FunkstrapSDK.ResetWindow()
	- For window's transparency / chroma key -> FunkstrapSDK.SetTransparency() and FunkstrapSDK.ResetTransparency()
	- For setting the window borderless state -> FunkstrapSDK.SetWindowBorderless()
	- For changing the window's colors -> FunkstrapSDK.SetWindowColor() and FunkstrapSDK.ResetWindowColor()
	- For changing wallpapers -> FunkstrapSDK.CacheWallpaper(), FunkstrapSDK.SetWallpaper() and FunkstrapSDK.ResetWallpaper()
	- For changing desktop visiblity -> FunkstrapSDK.SetDesktopVisibility() and FunkstrapSDK.ResetDesktopVisibility()
	- For sending notifications (Toasts) -> FunkstrapSDK.SendNotification()
]]

--[[
	Derivated from BloxstrapRPC Helper Module v1.0.1
	
	Written by pizzaboxer (@xtremeguy2256) and Epix (@ElusiveEpix)
	Types reflected from https://github.com/pizzaboxer/bloxstrap/tree/main/Bloxstrap/Models/BloxstrapRPC
	Further documentation is available at https://github.com/pizzaboxer/bloxstrap/wiki/Integrating-Bloxstrap-functionality-into-your-game
]]

local HttpService = game:GetService("HttpService")
local ContentProvider = game:GetService("ContentProvider");

local FunkstrapSDK = {}

const GAME_MESSAGE_PREFIX = "[BloxstrapRPC] ";
const RPC_RATELIMIT = 1 + 0.25;

--[=[
	These uri point to .png files that exist under the roblox 'content/bloxstrap' directory
	enabled.png exists only if the user has the activity tracking setting enabled
	%GAMEID%.png will only load if the user has given the universe permission to move the window,
	additionally, the pixels in this last image are a true/false describing the settings the user has enabled on bloxstrap's side
	
	These values are just for a sync-settings or to not flood logs, however they require EditableImages which are locked behind ID verification
]=]
const BLOXSTRAP_ENABLED_URI = "rbxasset://bloxstrap/enabled.png";
const GAME_ALLOWED_URI = `rbxasset://bloxstrap/{game.GameId}.png`;

const VALID_WALLPAPER_STYLES = { -- there's also checks on the funkstrap side
	Fill = true,
	Fit = true,
	Stretch = true,
	Tile = true,
	Center = true,
	Span = true,
}

type AssetId = number? | string? | Content?;
type Color = Color3? | string?;
type WallpaperStyle = "Fill"|"Fit"|"Stretch"|"Tile"|"Center"|"Span";

export type RichPresence = {
	details: string?|"<reset>",
	state: string?|"<reset>",
	timeStart: number?, -- set to 0 to remove
	timeEnd: number?, -- set to 0 to remove
	smallImage: RichPresenceImage?,
	largeImage: RichPresenceImage?,
}

export type RichPresenceImage = {
	assetId: AssetId,
	hoverText: string?,
	clear: boolean?,
	reset: boolean?,
}

export type WindowData = {
	x: number?,
	y: number?,
	width: number?,
	height: number?,

	scaleWidth: number?,
	scaleHeight: number?,

	reset: boolean?,
}

export type WindowTransparencyData = {
	transparency: number?;
	color: Color;
	useAlpha: boolean?;
}

export type NotificationData = {
	title: string?,
	caption: string?,
	duration: number?
}

export type WindowColorData = {
	caption: Color,
	border:	Color,
	reset: boolean?,
}

export type WallpaperData = {
	assetId: AssetId?, 
	style: WallpaperStyle?, -- "Fill", "Fit", "Stretch", "Tile", "Center", "Span" (Defaults to Fill)
	reset: boolean?, -- When true it restores the wallpaper that was set before the first call to SetWallpaper this session
}

export type DesktopData = {
	taskbar: boolean?,
	desktopIcons: boolean?,
	reset: boolean?,
}

function GetAssetId(asset: AssetId): number?
	if type(asset) == "number" then
		return asset
	elseif type(asset) == "string" then
		return tonumber(string.match(asset, "%d+"))
	elseif typeof(asset) == "Content" then
		return asset.Uri and tonumber(string.match(asset.Uri, "%d+")) or nil;
	end
	
	return nil;
end

function ToUInt32(color : Color3) : string
	local hexColor = color:ToHex();
	
	-- 0xbbggrr
	return "0x"..(string.sub(hexColor,5,6)..string.sub(hexColor,3,4)..string.sub(hexColor,1,2));
end

--[=[
	Returns the difference between two tables, used to calculate the difference between the previous and new window data passed
]=]
function makeDiff(a, b)
	local new = {};
	for k,v in b do
		new[k] = v;
	end

	for k,v in a do 
		if new[k]==v then new[k] = nil end
	end
	return new
end

--[=[
	Send a raw RPC message.
	Avoid using this, as every command available will have a dedicated function for it.
]=]
function FunkstrapSDK.SendMessage(command: string, data: any)
	local json = HttpService:JSONEncode({
		command = command,
		data = data,
	})

	print(GAME_MESSAGE_PREFIX .. json);
end

local lastRichPresenceUpdate = -1;
local rpcUpdateQueued = false;
local rpcUpdateData : RichPresence = {};

-- This has a mistake and it's that clear empties the image, reset sets image and hover text to default but both cannot be used at the same time
-- So if in any moment the queue receives a clear and reset separately, hover text may be wrong (priority: clear -> reset -> hoverText and assetId)
local function addImgChanges(key : string, newData : RichPresence)
	local newImage : RichPresenceImage? = newData[key];
	local updateImg : RichPresenceImage? = rpcUpdateData[key];
	
	if newImage then
		if updateImg then
			if newImage.assetId then
				updateImg.clear = false;
				updateImg.assetId = newImage.assetId;
			end
			
			updateImg.hoverText = newImage.hoverText;
			
			if newImage.reset then updateImg.reset = true; updateImg.hoverText = nil; updateImg.assetId = nil; end;
			if newImage.clear then updateImg.clear = true; updateImg.assetId = nil; end;
		else
			rpcUpdateData[key] = newImage;
		end
	end
end

--[=[
	Change or reset the user's Rich Presence activity if they are running Bloxstrap with the option enabled.
]=]
function FunkstrapSDK.SetRichPresence(data: RichPresence)
	if data.timeStart ~= nil then
		data.timeStart = math.round(data.timeStart)
	end
	if data.timeEnd ~= nil then
		data.timeEnd = math.round(data.timeEnd)
	end

	if data.smallImage ~= nil then
		data.smallImage.assetId = GetAssetId(data.smallImage.assetId)
	end
	if data.largeImage ~= nil then
		data.largeImage.assetId = GetAssetId(data.largeImage.assetId)
	end
	
	local t = tick();
	
	if t-lastRichPresenceUpdate < RPC_RATELIMIT then
		if not rpcUpdateQueued then
			task.delay(1,function()
				if next(rpcUpdateData) then
					FunkstrapSDK.SetRichPresence(rpcUpdateData);
					
					rpcUpdateData = {};
					rpcUpdateQueued = false;
				end
			end)
		end
		
		rpcUpdateQueued = true;
		
		-- Add changes
		do
			addImgChanges("smallImage", data);
			addImgChanges("largeImage", data);
			
			for k,v in data do
				if k ~= "smallImage" and k ~= "largeImage" then
					rpcUpdateData[k] = v;
				end
			end
		end
		
		return;
	end
	
	lastRichPresenceUpdate = t;

	FunkstrapSDK.SendMessage("SetRichPresence", data)
end

--[=[
	Set the launch data used for invite deeplinks
]=]
function FunkstrapSDK.SetLaunchData(data: string)
	FunkstrapSDK.SendMessage("SetLaunchData", data)
end

local function onGameAllowed()
	local editable: EditableImage?
	local settingsBuffer: buffer?
	
	pcall(function()
		editable = game:GetService("AssetService"):CreateEditableImageAsync(Content.fromUri(GAME_ALLOWED_URI));
		if editable then settingsBuffer = editable:ReadPixelsBuffer(Vector2.zero,Vector2.new(6,1)); end;
	end)

	if settingsBuffer then
		pcall(function()
			FunkstrapSDK.ExposedSettings = table.freeze({
				WindowControl 		= buffer.readu8(settingsBuffer,0+3) == 255;
				WindowTitle 		= buffer.readu8(settingsBuffer,4+3) == 255;
				WindowTransparency 	= buffer.readu8(settingsBuffer,8+3) == 255;
				WindowColor 		= buffer.readu8(settingsBuffer,12+3) == 255;
				WallpaperControl 	= buffer.readu8(settingsBuffer,16+3) == 255;
				DesktopControl 		= buffer.readu8(settingsBuffer,20+3) == 255;
			})
			
			print("[Debug] Imported settings from Funkstrap", HttpService:JSONEncode(FunkstrapSDK.ExposedSettings));
		end);
		
		(editable :: EditableImage):Destroy();
		editable = nil;
	end
end

ContentProvider:PreloadAsync({BLOXSTRAP_ENABLED_URI, GAME_ALLOWED_URI});
ContentProvider:Preload(BLOXSTRAP_ENABLED_URI);
ContentProvider:Preload(GAME_ALLOWED_URI);

FunkstrapSDK.UsesBloxstrap = ContentProvider:GetAssetFetchStatus(BLOXSTRAP_ENABLED_URI) == Enum.AssetFetchStatus.Success;
FunkstrapSDK.IsGameAllowed = FunkstrapSDK.UsesBloxstrap and (ContentProvider:GetAssetFetchStatus(GAME_ALLOWED_URI) == Enum.AssetFetchStatus.Success);
FunkstrapSDK.WindowStarted = false; -- Replicates the method whitelist on window control methods when StartWindow is not called (only allows SetTitle)

FunkstrapSDK.ExposedSettings = table.freeze({}) :: {
	WindowControl 		: boolean?;
	WindowTitle 		: boolean?;
	WindowTransparency 	: boolean?;
	WindowColor 		: boolean?;
	WallpaperControl 	: boolean?;
	DesktopControl 		: boolean?;
};

if FunkstrapSDK.UsesBloxstrap and not FunkstrapSDK.IsGameAllowed then
	local fetchStatusChanged : RBXScriptConnection?; fetchStatusChanged = ContentProvider:GetAssetFetchStatusChangedSignal(GAME_ALLOWED_URI):Connect(function(status : Enum.AssetFetchStatus)
		if status == Enum.AssetFetchStatus.Success then
			(fetchStatusChanged :: RBXScriptConnection):Disconnect();
			fetchStatusChanged = nil;

			FunkstrapSDK.IsGameAllowed = true;
			--print("[Bloxstrap] Game is allowed");

			onGameAllowed();
		end
	end)
elseif FunkstrapSDK.IsGameAllowed then
	onGameAllowed();
end

--[=[
	Checks if the game was given perms, call this method to update the IsGameAllowed variable
]=]
function FunkstrapSDK.CheckWindowPermission()
	if not FunkstrapSDK.UsesBloxstrap or FunkstrapSDK.IsGameAllowed then return; end;
	ContentProvider:Preload(GAME_ALLOWED_URI);
end

--[=[
	Popups up a window asking the user if they want to allow the game to control the window through Bloxstrap, it can only be used once per session,
	so if the user denies (without marking the dont ask again) or closes the window, they'll have to rejoin, this is to avoid spam from the game
	
	It's recommended that you use a popup (which should only display if bloxstrap is detected) with a simple explanation and let the user click a button to request permssions themselves
]=]
function FunkstrapSDK.RequestWindowPermission()
	if not FunkstrapSDK.UsesBloxstrap or FunkstrapSDK.IsGameAllowed then return end;
	FunkstrapSDK.SendMessage("RequestWindowPermission", nil)
end

--[=[
	These tables hold the cached values for win pos and size
]=]
local prevWinData: WindowData = {};
local prevTransWinData: WindowTransparencyData = {};

--[=[
	Call this when you want to control the window in any way
	
	Also changes the log speed from checking every second to checking on the rate the user has selected (This is what the window control fps setting is for), which means there's more
	work on bloxstrap's side by checking the logs more frequently so it's better to call StopWindow when the window is no longer gonna be moved.
	
	This is currently in a weird state because before, commands were locked behind you needing to call StartWindow, this is no longer the case
	It's only purpose is to increase the log read speed (Before it was also saving the window state but that's done separately now)
]=]
function FunkstrapSDK.StartWindow()
	FunkstrapSDK.WindowStarted = true;
	FunkstrapSDK.SendMessage("StartWindow", nil)
	
	prevWinData = {};
end

--[=[
	Resets the log speed to 1s, and resets the window position and size to what it was before any SetWindow call was made
]=]
function FunkstrapSDK.StopWindow()
	FunkstrapSDK.WindowStarted = false;
	FunkstrapSDK.SendMessage("StopWindow", nil)
	
	prevWinData = {};
end

--[=[
	Sets a window position and size based on a given position, size and the screen size they are based on for scaling on other displays
	This method is optimized to only send data that is different to reduce a big increase in roblox's log size
	Every single method on bloxstrap's side caches the values you pass so you can avoid passing them and save some log size
]=]
function FunkstrapSDK.SetWindow(data: WindowData)
	if not FunkstrapSDK.IsGameAllowed then return end;

	if data.reset then
		FunkstrapSDK.ResetWindow();
		return;
	end

	data.reset = nil;

	for k,v in data do
		data[k] = math.round(v :: number)
	end

	local diff = makeDiff(prevWinData,data)
	if not next(diff) then return end;

	for k,v in diff do
		prevWinData[k] = v;
	end

	FunkstrapSDK.SendMessage("SetWindow", diff)
end

--[=[
	Restores the window to the position and size it was before StartWindow was called,
	also called on bloxstrap on StopWindow
]=]
function FunkstrapSDK.ResetWindow()
	if not FunkstrapSDK.IsGameAllowed then return end;

	FunkstrapSDK.SendMessage("ResetWindow");
	prevWinData = {};
end

--[=[
	Sets the window title if the user has the window control and the title control settings enabled
	
	Works without user permission
]=]
function FunkstrapSDK.SetTitle(data: string?)
	if not data then return FunkstrapSDK.ResetTitle(); end;

	FunkstrapSDK.SendMessage("SetWindowTitle", data);
end

--[=[
	Same as calling .SetTitle with nil, funkstrap will reset the title to 'Roblox' when no name argument is passed
	You can change this to your game name
]=]
function FunkstrapSDK.ResetTitle()
	FunkstrapSDK.SendMessage("SetWindowTitle", "Roblox")
end

--[=[
	Makes the window behave like a chroma key / transparent image, where transparency is actually alpha (1-robloxTransparency) or (0=Fully Transparency and 1=Fully Visible)
	and color is the color that will be keyed, its only the exact color,
	green is usually recommended since its not common and you can easily add a 0.98 transparency black frame to avoid full green color from showing where you dont want to.

	if useAlpha is passed as true, the chroma key color will not be used, additionally due to limitations
	-> If chroma keys are used, the window will be either fully transparent or fully opaque and every pixel the same color of the chroma key will be able to be click through (If alpha != 1, it's transparent)
	-> If alpha is used, the window will be able to have any transparency and will not be able to be click through (unless fully transparent)
]=]
function FunkstrapSDK.SetTransparency(data: WindowTransparencyData)
	if not FunkstrapSDK.IsGameAllowed then return end;

	local diff = makeDiff(prevTransWinData,data)
	if not next(diff) then return end;
	
	if typeof(diff.color) == "Color3" then
		diff.color = ToUInt32(diff.color);
	end

	for k,v in diff do
		prevTransWinData[k] = v;
	end

	FunkstrapSDK.SendMessage("SetWindowTransparency",diff)
end

--[=[
	Resets the transparency to 1, making the window fully visible
	Keeps the last sent color and transparency mode (chroma key or alpha)
]=]
function FunkstrapSDK.ResetTransparency()
	if not FunkstrapSDK.IsGameAllowed then return end;

	FunkstrapSDK.SendMessage("SetWindowTransparency", {
		transparency = 1;
	})

	prevTransWinData.transparency = 1;
end

--[=[
	Makes the window borderless
]=]
function FunkstrapSDK.SetWindowBorderless(status: boolean)
	if not FunkstrapSDK.IsGameAllowed then return end;
	
	FunkstrapSDK.SendMessage("SetWindowBorderless", status or false)
end

--[=[
	Sends a Windows toast to the user with the given title, caption and duration
]=]
function FunkstrapSDK.SendNotification(notificationData: NotificationData)
	if not FunkstrapSDK.IsGameAllowed then return end;
	
	FunkstrapSDK.SendMessage("SendNotification", notificationData)
end

--[=[
	Sets the color of a window's title (caption), or it's border
]=]
function FunkstrapSDK.SetWindowColor(data: WindowColorData)
	if not FunkstrapSDK.IsGameAllowed then return end;
	
	if data.reset then
		FunkstrapSDK.ResetWindowColor();
		return;
	end
	
	data.reset = nil;
	
	if typeof(data.border) == "Color3" then
		data.border = ToUInt32(data.border);
	end
	
	if typeof(data.caption) == "Color3" then
		data.caption = ToUInt32(data.caption);
	end
	
	FunkstrapSDK.SendMessage("SetWindowColor", data)
end

--[=[
	Resets the window's title and border color's to defaults
]=]
function FunkstrapSDK.ResetWindowColor()
	if not FunkstrapSDK.IsGameAllowed then return end;
	
	FunkstrapSDK.SendMessage("SetWindowColor", { reset = true });
end

--[=[
	Caches a wallpaper to be used later on SetWallpaper
]=]
function FunkstrapSDK.CacheWallpaper(assetId: AssetId)
	if not FunkstrapSDK.IsGameAllowed then return end;
	
	assetId = GetAssetId(assetId);
	if not assetId then return; end;

	FunkstrapSDK.SendMessage("CacheWallpaper", assetId)
end

--[=[
	Sets a wallpaper with the given assetId and style
]=]
function FunkstrapSDK.SetWallpaper(data: WallpaperData)
	if not FunkstrapSDK.IsGameAllowed then return end;
	
	data.assetId = GetAssetId(data.assetId);

	if data.reset or not data.assetId then
		FunkstrapSDK.ResetWallpaper();
		return;
	end
	
	data.reset = nil;

	if not VALID_WALLPAPER_STYLES[data.style or -1] then
		data.style = "Fill"
	end

	FunkstrapSDK.SendMessage("SetWallpaper", data)
end

--[=[
	Resets the wallpaper to the user's
]=]
function FunkstrapSDK.ResetWallpaper()
	if not FunkstrapSDK.IsGameAllowed then return end;
	FunkstrapSDK.SendMessage("SetWallpaper", { reset = true })
end

--[=[
	Changes the visibility of the desktop icons or the taskbar
]=]
function FunkstrapSDK.SetDesktopVisibility(data: DesktopData)
	if not FunkstrapSDK.IsGameAllowed then return end

	if data.reset then
		FunkstrapSDK.ResetDesktopVisibility();
		return;
	end
	
	data.reset = nil;

	if next(data) then
		FunkstrapSDK.SendMessage("SetDesktopVisibility", data);
	end
end

--[=[
	Resets the visibility of the desktop icons or the taskbar to defaults (Visible)
]=]
function FunkstrapSDK.ResetDesktopVisibility()
	if not FunkstrapSDK.IsGameAllowed then return end

	FunkstrapSDK.SendMessage("SetDesktopVisibility", { reset = true });
end

return FunkstrapSDK;