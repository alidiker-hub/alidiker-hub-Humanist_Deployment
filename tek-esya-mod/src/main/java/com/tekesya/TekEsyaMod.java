package com.tekesya;

import net.minecraftforge.eventbus.api.IEventBus;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext;

@Mod(TekEsyaMod.MOD_ID)
public class TekEsyaMod {
    public static final String MOD_ID = "tekesya";

    public TekEsyaMod(FMLJavaModLoadingContext context) {
        IEventBus bus = context.getModEventBus();
        ModItems.register(bus);
    }
}
