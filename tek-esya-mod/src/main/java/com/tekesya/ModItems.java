package com.tekesya;

import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraftforge.eventbus.api.IEventBus;
import net.minecraftforge.registries.DeferredRegister;
import net.minecraftforge.registries.ForgeRegistries;
import net.minecraftforge.registries.RegistryObject;

/**
 * Tek esya burada kayit edilir.
 * Yeni esya eklemek istersen ayni kaliba bir satir daha yazman yeterli.
 */
public final class ModItems {
    public static final DeferredRegister<Item> ITEMS =
            DeferredRegister.create(ForgeRegistries.ITEMS, TekEsyaMod.MOD_ID);

    public static final DeferredRegister<CreativeModeTab> TABS =
            DeferredRegister.create(Registries.CREATIVE_MODE_TAB, TekEsyaMod.MOD_ID);

    /** Yaratıcı menüdeki mor kare esya. */
    public static final RegistryObject<Item> ORNEK_ESYA = ITEMS.register("ornek_esya",
            () -> new Item(new Item.Properties()));

    public static final RegistryObject<CreativeModeTab> TEK_ESYA_SEKME = TABS.register("tek_esya",
            () -> CreativeModeTab.builder()
                    .title(Component.translatable("itemGroup.tekesya"))
                    .icon(() -> new ItemStack(ORNEK_ESYA.get()))
                    .displayItems((params, output) -> output.accept(ORNEK_ESYA.get()))
                    .build());

    private ModItems() {
    }

    public static void register(IEventBus bus) {
        ITEMS.register(bus);
        TABS.register(bus);
    }
}
