package com.tekesya;

import net.minecraftforge.eventbus.api.IEventBus;
import net.minecraftforge.fml.common.Mod;

@Mod(TekEsyaMod.MOD_ID)
public class TekEsyaMod {
    public static final String MOD_ID = "tekesya";

    public TekEsyaMod(IEventBus modEventBus) {
        ModItems.register(modEventBus);
    }
}
