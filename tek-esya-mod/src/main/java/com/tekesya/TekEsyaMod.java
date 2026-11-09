package com.tekesya;

import com.mojang.logging.LogUtils;
import net.minecraftforge.eventbus.api.IEventBus;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext;
import org.slf4j.Logger;

/**
 * Modun giris noktasi.
 * Esyayi degistirmek icin {@link ModItems} dosyasina bak.
 */
@Mod(TekEsyaMod.MOD_ID)
public class TekEsyaMod {
    public static final String MOD_ID = "tekesya";
    public static final Logger LOGGER = LogUtils.getLogger();

    public TekEsyaMod(FMLJavaModLoadingContext context) {
        IEventBus bus = context.getModEventBus();
        ModItems.register(bus);
        LOGGER.info("Tek Esya modu yuklendi");
    }
}
