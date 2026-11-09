package com.tekesya;

import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.SwordItem;
import net.minecraft.world.item.Tiers;
import net.minecraftforge.eventbus.api.IEventBus;
import net.minecraftforge.registries.DeferredRegister;
import net.minecraftforge.registries.ForgeRegistries;
import net.minecraftforge.registries.RegistryObject;

public final class ModItems {
    public static final DeferredRegister<Item> ITEMS =
            DeferredRegister.create(ForgeRegistries.ITEMS, TekEsyaMod.MOD_ID);

    public static final DeferredRegister<CreativeModeTab> TABS =
            DeferredRegister.create(Registries.CREATIVE_MODE_TAB, TekEsyaMod.MOD_ID);

    public static final RegistryObject<Item> KILIC = ITEMS.register("kilic",
            () -> new SwordItem(Tiers.IRON, 3, -2.4F, new Item.Properties()));

    public static final RegistryObject<CreativeModeTab> SEKME = TABS.register("kilic",
            () -> CreativeModeTab.builder()
                    .title(Component.translatable("itemGroup.tekesya"))
                    .icon(() -> new ItemStack(KILIC.get()))
                    .displayItems((params, output) -> output.accept(KILIC.get()))
                    .build());

    private ModItems() {
    }

    public static void register(IEventBus bus) {
        ITEMS.register(bus);
        TABS.register(bus);
    }
}
