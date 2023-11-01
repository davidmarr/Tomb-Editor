-- !Name "Run event set ..."
-- !Section "Logic"
-- !Conditional "False"
-- !Description "Run event set only under certain conditions of the game"
-- !Arguments "NewLine, 70, EventSets, Event set"
-- !Arguments "Enumeration, 30, [ When entering | When inside | When Leaving ], Event-set section"
-- !Arguments "NewLine, Moveables, Activator for the event-set (when necessary)"
-- !Arguments "NewLine, Enumeration,18, [before | after ], Before or after the selected context"
-- !Arguments "Enumeration,59, [ saving the game | Loading the save game | exit to the title | level is completed | Lara's death | each cycle of the game (frame) ], context when to run the event set"
--!Arguments "Number, 23, [ 1 | 30 | 0 ], Slot where to save the event set\nRange [1 to 40]"
LevelFuncs.Engine.Node.AddCallbackEventSet2 = function(setName, eventType, activator, when, constest, slot)
    if (setName == '' or setName == nil) then
        print("There is no specified event set in level!")
        return
    end
    local CallbackPoint = nil
    local endReason = nil
    CallbackPoint = ((when == 0) and (constest == 0)) and 0 or
        ((when == 1) and (constest == 0)) and 1 or
        ((when == 0) and (constest == 1)) and 2 or
        ((when == 1) and (constest == 1)) and 3 or
        (((when == 0) and (constest == 2)) or ((when == 0) and (constest == 3)) or ((when == 0) and (constest == 4))) and
        4 or
        (((when == 1) and (constest == 2)) or ((when == 1) and (constest == 3)) or ((when == 1) and (constest == 4))) and
        5 or
        ((when == 0) and (constest == 5)) and 6 or
        ((when == 1) and (constest == 5)) and 7
    endReason = (constest == 2) and 0 or (constest == 3) and 1 or (constest == 4) and 2 or nil
    LevelVars.CBs[slot] = {}
    LevelVars.CBs[slot].setName = setName
    LevelVars.CBs[slot].eventType = eventType
    LevelVars.CBs[slot].activator = activator
    LevelVars.CBs[slot].callbackP = CallbackPoint
    LevelVars.CBs[slot].endReason = endReason
    LevelVars.CBs[slot].constest = constest
    LevelVars.CBs[slot].when = when
    local func = AssignFunction(slot)
    TEN.Logic.AddCallback(LevelVars.CBES.CallbackPoint[CallbackPoint], func)
    local eventTypeT = LevelVars.CBES.eventTypeList[LevelVars.CBs[slot].eventType]
    local whenT = (when == 0) and " before " or " after "
    local constestT = LevelVars.CBES.ConstestList[constest]
    print('"Run Evet set ' .. setName .. ' (' .. eventTypeT .. ')' .. whenT .. constestT .. '" saved in slot ' .. slot)
end

-- !Name "Remove 'Run event set ...' from slot"
-- !Section "Logic"
-- !Conditional "False"
-- !Description "Remove the event set from the specified slot"
-- !Arguments "NewLine, Number, 100, [ 1 | 40 | 0 ], Slot \nRange [1 to 40]"
LevelFuncs.Engine.Node.RemoveCallbackEventSet = function(slot)
    if LevelVars.CBs[slot].setName == nil then
        print("Empty slot")
    else
        local func = AssignFunction(slot)
        local cBp = LevelVars.CBES.CallbackPoint[LevelVars.CBs[slot].callbackP]
        local setName = LevelVars.CBs[slot].setName
        local evenTypeT = LevelVars.CBES.eventTypeList[LevelVars.CBs[slot].eventType]
        local constestT = LevelVars.CBES.ConstestList[LevelVars.CBs[slot].constest]
        local whenT = (LevelVars.CBs[slot].when == 0) and " before " or " after "
        print('Removed "Run Evet set ' ..
            setName .. ' (' .. evenTypeT .. ')' .. whenT .. constestT .. '" from slot ' .. slot)
        TEN.Logic.RemoveCallback(cBp, func)
        LevelVars.CBs[slot].setName = nil
        LevelVars.CBs[slot].eventType = nil
        LevelVars.CBs[slot].callbackP = nil
        LevelVars.CBs[slot].endReason = nil
        LevelVars.CBs[slot].situation = nil
        LevelVars.CBs[slot].when = nil
    end
end


LevelVars.CBs                = {}
LevelVars.CBES               = {}
LevelVars.CBES.eventTypeList = { [0] = "When entering", [1] = "When inside", [2] = "When Leaving" }
LevelVars.CBES.CallbackPoint =
{
    [0] = TEN.Logic.CallbackPoint.PRESAVE,
    [1] = TEN.Logic.CallbackPoint.POSTSAVE,
    [2] = TEN.Logic.CallbackPoint.PRELOAD,
    [3] = TEN.Logic.CallbackPoint.POSTLOAD,
    [4] = TEN.Logic.CallbackPoint.PREEND,
    [5] = TEN.Logic.CallbackPoint.POSTEND,
    [6] = TEN.Logic.CallbackPoint.PRECONTROLPHASE,
    [7] = TEN.Logic.CallbackPoint.POSTCONTROLPHASE
}
LevelVars.CBES.ConstestList  = {
    [0] = "Saving the game",
    [1] = "Loading of the save game",
    [2] = "Exit to the title",
    [3] = "Level is completed",
    [4] = "Lara's death",
    [5] = "Each game cycle (frame)",
}

function HandleEvent(number, arg)
    local callbackVals = LevelVars.CBs[number]
    local checkLevelEndReason = false
    if callbackVals.callbackP == 4 or callbackVals.callbackP == 5 then
        print("LevelEndReason " .. arg)
        if (arg == TEN.Logic.EndReason.EXITTOTITLE) and callbackVals.endReason == 0 then
            print("LevelEndReason EXITTOTITLE")
            checkLevelEndReason = true
        end
        if (arg == TEN.Logic.EndReason.LEVELCOMPLETE) and callbackVals.endReason == 1 then
            print("LevelEndReason LEVELCOMPLETE")
            checkLevelEndReason = true
        end
        if (arg == TEN.Logic.EndReason.DEATH) and callbackVals.endReason == 2 then
            print("LevelEndReason DEATH")
            checkLevelEndReason = true
        end
        if (arg == TEN.Logic.EndReason.OTHER) then
            print("OTHER")
        end
    else
        checkLevelEndReason = true
    end
    if checkLevelEndReason == true then
        TEN.Logic.HandleEvent(callbackVals.setName, callbackVals.eventType,
            TEN.Objects.GetMoveableByName(callbackVals.activator))
    end
end

function AssignFunction(slot)
    local func = nil
    func =
        (slot == 1) and LevelFuncs.CallbackEventSet1 or
        (slot == 2) and LevelFuncs.CallbackEventSet2 or
        (slot == 3) and LevelFuncs.CallbackEventSet3 or
        (slot == 4) and LevelFuncs.CallbackEventSet4 or
        (slot == 5) and LevelFuncs.CallbackEventSet5 or
        (slot == 6) and LevelFuncs.CallbackEventSet6 or
        (slot == 7) and LevelFuncs.CallbackEventSet7 or
        (slot == 8) and LevelFuncs.CallbackEventSet8 or
        (slot == 9) and LevelFuncs.CallbackEventSet9 or
        (slot == 10) and LevelFuncs.CallbackEventSet10 or
        (slot == 11) and LevelFuncs.CallbackEventSet11 or
        (slot == 12) and LevelFuncs.CallbackEventSet12 or
        (slot == 13) and LevelFuncs.CallbackEventSet13 or
        (slot == 14) and LevelFuncs.CallbackEventSet14 or
        (slot == 15) and LevelFuncs.CallbackEventSet15 or
        (slot == 16) and LevelFuncs.CallbackEventSet16 or
        (slot == 17) and LevelFuncs.CallbackEventSet17 or
        (slot == 18) and LevelFuncs.CallbackEventSet18 or
        (slot == 19) and LevelFuncs.CallbackEventSet19 or
        (slot == 20) and LevelFuncs.CallbackEventSet20 or
        (slot == 21) and LevelFuncs.CallbackEventSet21 or
        (slot == 22) and LevelFuncs.CallbackEventSet22 or
        (slot == 23) and LevelFuncs.CallbackEventSet23 or
        (slot == 24) and LevelFuncs.CallbackEventSet24 or
        (slot == 25) and LevelFuncs.CallbackEventSet25 or
        (slot == 26) and LevelFuncs.CallbackEventSet26 or
        (slot == 27) and LevelFuncs.CallbackEventSet27 or
        (slot == 28) and LevelFuncs.CallbackEventSet28 or
        (slot == 29) and LevelFuncs.CallbackEventSet29 or
        (slot == 30) and LevelFuncs.CallbackEventSet30 or
        (slot == 31) and LevelFuncs.CallbackEventSet31 or
        (slot == 32) and LevelFuncs.CallbackEventSet32 or
        (slot == 33) and LevelFuncs.CallbackEventSet33 or
        (slot == 34) and LevelFuncs.CallbackEventSet34 or
        (slot == 35) and LevelFuncs.CallbackEventSet35 or
        (slot == 36) and LevelFuncs.CallbackEventSet36 or
        (slot == 37) and LevelFuncs.CallbackEventSet37 or
        (slot == 38) and LevelFuncs.CallbackEventSet38 or
        (slot == 39) and LevelFuncs.CallbackEventSet39 or
        (slot == 40) and LevelFuncs.CallbackEventSet40
    return func
end

LevelFuncs.CallbackEventSet1  = function(arg) HandleEvent(1, arg) end
LevelFuncs.CallbackEventSet2  = function(arg) HandleEvent(2, arg) end
LevelFuncs.CallbackEventSet3  = function(arg) HandleEvent(3, arg) end
LevelFuncs.CallbackEventSet4  = function(arg) HandleEvent(4, arg) end
LevelFuncs.CallbackEventSet5  = function(arg) HandleEvent(5, arg) end
LevelFuncs.CallbackEventSet6  = function(arg) HandleEvent(6, arg) end
LevelFuncs.CallbackEventSet7  = function(arg) HandleEvent(7, arg) end
LevelFuncs.CallbackEventSet8  = function(arg) HandleEvent(8, arg) end
LevelFuncs.CallbackEventSet9  = function(arg) HandleEvent(9, arg) end
LevelFuncs.CallbackEventSet10 = function(arg) HandleEvent(10, arg) end
LevelFuncs.CallbackEventSet11 = function(arg) HandleEvent(11, arg) end
LevelFuncs.CallbackEventSet12 = function(arg) HandleEvent(12, arg) end
LevelFuncs.CallbackEventSet13 = function(arg) HandleEvent(13, arg) end
LevelFuncs.CallbackEventSet14 = function(arg) HandleEvent(14, arg) end
LevelFuncs.CallbackEventSet15 = function(arg) HandleEvent(15, arg) end
LevelFuncs.CallbackEventSet16 = function(arg) HandleEvent(16, arg) end
LevelFuncs.CallbackEventSet17 = function(arg) HandleEvent(17, arg) end
LevelFuncs.CallbackEventSet18 = function(arg) HandleEvent(18, arg) end
LevelFuncs.CallbackEventSet19 = function(arg) HandleEvent(19, arg) end
LevelFuncs.CallbackEventSet20 = function(arg) HandleEvent(20, arg) end
LevelFuncs.CallbackEventSet21 = function(arg) HandleEvent(21, arg) end
LevelFuncs.CallbackEventSet22 = function(arg) HandleEvent(22, arg) end
LevelFuncs.CallbackEventSet23 = function(arg) HandleEvent(23, arg) end
LevelFuncs.CallbackEventSet24 = function(arg) HandleEvent(24, arg) end
LevelFuncs.CallbackEventSet25 = function(arg) HandleEvent(25, arg) end
LevelFuncs.CallbackEventSet26 = function(arg) HandleEvent(26, arg) end
LevelFuncs.CallbackEventSet27 = function(arg) HandleEvent(27, arg) end
LevelFuncs.CallbackEventSet28 = function(arg) HandleEvent(28, arg) end
LevelFuncs.CallbackEventSet29 = function(arg) HandleEvent(29, arg) end
LevelFuncs.CallbackEventSet30 = function(arg) HandleEvent(30, arg) end
LevelFuncs.CallbackEventSet31 = function(arg) HandleEvent(31, arg) end
LevelFuncs.CallbackEventSet32 = function(arg) HandleEvent(32, arg) end
LevelFuncs.CallbackEventSet33 = function(arg) HandleEvent(33, arg) end
LevelFuncs.CallbackEventSet34 = function(arg) HandleEvent(34, arg) end
LevelFuncs.CallbackEventSet35 = function(arg) HandleEvent(35, arg) end
LevelFuncs.CallbackEventSet36 = function(arg) HandleEvent(36, arg) end
LevelFuncs.CallbackEventSet37 = function(arg) HandleEvent(37, arg) end
LevelFuncs.CallbackEventSet38 = function(arg) HandleEvent(38, arg) end
LevelFuncs.CallbackEventSet39 = function(arg) HandleEvent(39, arg) end
LevelFuncs.CallbackEventSet40 = function(arg) HandleEvent(40, arg) end
