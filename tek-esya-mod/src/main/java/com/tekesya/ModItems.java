package com.tekesya;

import com.google.common.collect.HashMultimap;
import com.google.common.collect.Multimap;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.ai.attributes.Attribute;
import net.minecraft.world.entity.ai.attributes.AttributeModifier;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.item.CreativeModeTab;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.SwordItem;
import net.minecraft.world.item.Tier;
import net.minecraft.world.item.crafting.Ingredient;
import net.minecraftforge.eventbus.api.IEventBus;
import net.minecraftforge.registries.DeferredRegister;
import net.minecraftforge.registries.ForgeRegistries;
import net.minecraftforge.registries.RegistryObject;

public final class ModItems {
    // Degerler: DEGISTIRME.md — Texture: src/main/resources/assets/tekesya/textures/item/kilic.png
    public static final int HASAR = 6;
    public static final double HIZ = 1.6;
    public static final int DAYANIKLILIK = 250;

    public static final DeferredRegister<Item> ITEMS =
            DeferredRegister.create(ForgeRegistries.ITEMS, TekEsyaMod.MOD_ID);

    public static final DeferredRegister<CreativeModeTab> TABS =
            DeferredRegister.create(Registries.CREATIVE_MODE_TAB, TekEsyaMod.MOD_ID);

    public static final RegistryObject<Item> KILIC = ITEMS.register("kilic", Kilic::new);

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

    private static final class Kilic extends SwordItem {
        private static final Tier TIER = new Tier() {
            public int getUses() {
                return DAYANIKLILIK;
            }

            public float getSpeed() {
                return 1.0F;
            }

            public float getAttackDamageBonus() {
                return 0;
            }

            public int getLevel() {
                return 2;
            }

            public int getEnchantmentValue() {
                return 14;
            }

            public Ingredient getRepairIngredient() {
                return Ingredient.of(Items.IRON_INGOT);
            }
        };

        private Kilic() {
            super(TIER, 0, 0, new Item.Properties());
        }

        @Override
        public Multimap<Attribute, AttributeModifier> getDefaultAttributeModifiers(EquipmentSlot slot) {
            Multimap<Attribute, AttributeModifier> map = HashMultimap.create();
            if (slot == EquipmentSlot.MAINHAND) {
                map.put(Attributes.ATTACK_DAMAGE, new AttributeModifier(
                        BASE_ATTACK_DAMAGE_UUID, "Weapon modifier", HASAR - 1, AttributeModifier.Operation.ADDITION));
                map.put(Attributes.ATTACK_SPEED, new AttributeModifier(
                        BASE_ATTACK_SPEED_UUID, "Weapon modifier", HIZ - 4.0, AttributeModifier.Operation.ADDITION));
            }
            return map;
        }
    }
}
