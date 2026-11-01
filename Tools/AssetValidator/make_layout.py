"""Builds Data/Layout/layout.json from a slot table, including only equipment that has an entry in Data/Equipment/equipment.json
(i.e. whose model was converted). Positions are in metres in a 10 x 8 m room: x -5..5 (left..right), z -4..4 (entrance..back wall).
Items with `on` rest on top of another item (dx/dz are offsets from that item's centre). Edit the slot table, rerun, rebuild."""
import io, json, os

R = 'D:/MedSimXR/'

# id: (x, y, z, yaw)  or  id: ('on', host, dx, dz, yaw)
SLOTS = {
    # --- fixed core (unchanged since Phase 1) ---
    "chair_patient_mobile_a01": (2.6, 0, -0.9, 200),
    "CT_injection_system_a01": (4.25, 0, 2.9, 270),
    "rack_large_empty_a02": (-2.8, 0, 2.85, 180),
    "table_metal_stainless_a03": (0.9, 0, 2.6, 0),
    "box_corrugated_brown_b20": ('on', "table_metal_stainless_a03", -0.5, 0.0, 90),
    # --- patient bay (front left) ---
    "bed_hospital_standard_a01": (-2.6, 0, -2.9, 0),
    "table_bedside_b01": (-4.6, 0, -1.6, 90),
    "IV_pole_a01": (-3.0, 0, -1.5, 0),
    "monitor_patient_a01": (-4.5, 0, -3.6, 0),
    "light_head_bed_a01": (-2.6, 1.9, -3.9, 0),
    "unit_head_bed_a01": (-2.6, 1.3, -3.9, 0),
    "pump_infusion_a01": ('on', "table_bedside_b01", 0.0, 0.0, 90),
    "stethoscope_electronic_a01": ('on', "bed_hospital_standard_a01", 0.8, 0.0, 0),
    "lung_ventilation_artificial_a01": (-0.4, 0, -3.5, 0),
    # --- supplies and instruments (front middle) ---
    "cart_medical_utility_a01": (-1.2, 0, -1.8, 0),
    "trolley_instrument_b01": (-1.2, 0, -0.3, 0),
    "instrument_artery_forceps_b01": ('on', "trolley_instrument_b01", -0.3, -0.12, 0),
    "instrument_bone_forceps_b01": ('on', "trolley_instrument_b01", -0.3, 0.12, 0),
    "instrument_bone_rongeurs_b01": ('on', "trolley_instrument_b01", 0.0, -0.12, 0),
    "instrument_bone_mallet_b01": ('on', "trolley_instrument_b01", 0.0, 0.12, 0),
    "instrument_hand_drill_b01": ('on', "trolley_instrument_b01", 0.3, 0.0, 0),
    "needle_surgical_a01": ('on', "trolley_instrument_b01", 0.15, 0.0, 0),
    "stapler_surgical_a01": ('on', "trolley_instrument_b01", -0.15, 0.0, 0),
    "tool_pen_surgical_marking_a01": ('on', "trolley_instrument_b01", 0.3, 0.17, 0),
    "tool_power_surgical_b01": ('on', "cart_medical_utility_a01", 0.0, 0.0, 0),
    "tray_sterilization_instrument_c02": ('on', "trolley_instrument_b01", 0.0, 0.0, 0),
    # --- hand hygiene (front wall) ---
    "washBasin_a01": (1.8, 0.8, -3.7, 0),
    "cleaning_soapDispenser_a01": (1.8, 1.25, -3.95, 0),
    "appliance_handDryer_automatic_a01": (2.6, 1.0, -3.95, 0),
    "sink_surgical_a01": (0.6, 0, -3.6, 0),
    "bin_waste_a01": (4.4, 0, -3.4, 0),
    "trolley_cleaning_a01": (3.3, 0, -3.5, 0),
    "disinfectant_a01": ('on', "table_metal_stainless_a03", 0.05, 0.0, 0),
    # --- right side: mobility, emergency, imaging ---
    "wheelChair_c01": (3.9, 0, -2.2, 180),
    "trolley_emergency_a01": (4.4, 0, -1.2, 270),
    "defibrillator_a01": ('on', "trolley_emergency_a01", 0.0, 0.0, 270),
    "Xray_machine_buckyStand_a01": (4.5, 0, 0.6, 270),
    "Xray_machine_buckyTable_a01": (2.2, 0, 0.9, 0),
    "Xray_machine_HVgenerator_a01": (3.2, 0, 2.3, 0),
    "Xray_machine_XrayArmRack_a01": (2.2, 0, 1.6, 0),
    "chair_hospital_a01": (3.3, 0, -3.0, 0),
    # --- left side: storage, surgery ---
    "cabinet_for_drugs_a01": (-4.55, 0, 0.8, 90),
    "warmer_infusion_a01": ('on', "cabinet_for_drugs_a01", 0.0, 0.0, 90),
    "robot_surgical_c01": (-3.0, 0, 0.9, 0),
    "table_surgical_a01": (-2.0, 0, 0.9, 0),
    "light_surgical_ceiling_mobile_a01": (-3.8, 0, 1.6, 0),
    "anesthesia_machine_a01": (-0.6, 0, 1.4, 0),
    "unit_electrosurgical_a01": (-0.6, 0, 0.4, 0),
    "pump_suction_surgical_a01": (-1.8, 0, 1.6, 0),
    "laser_system_a01": (0.6, 0, -0.6, 0),
    "hemodialysis_machine_a01": (-4.4, 0, -0.4, 90),
    "device_endoscopy_system_b01": (-0.1, 0, 0.0, 0),
    "robotics_small_bot_a01": (1.2, 0, -1.5, 0),
    # --- small handheld items on the inspection table ---
    "syringe_a01": ('on', "table_metal_stainless_a03", -0.8, -0.2, 0),
    "thermometer_fever_a01": ('on', "table_metal_stainless_a03", -0.8, 0.2, 0),
    "medicine_a03": ('on', "table_metal_stainless_a03", -0.25, -0.2, 0),
    "box_glove_a01": ('on', "table_metal_stainless_a03", -0.25, 0.2, 0),
    "anesthesia_mask_a01": ('on', "table_metal_stainless_a03", 0.05, 0.22, 0),
    "device_vein_finder_a01": ('on', "table_metal_stainless_a03", -0.55, 0.24, 0),
    "device_endoscope_a01": ('on', "table_metal_stainless_a03", -0.55, -0.24, 0),
    "drape_surgical_a01": ("on", "table_bedside_b01", 0.0, 0.0, 0),
    "headlight_surgical_a01": ('on', "cart_medical_utility_a01", 0.0, 0.2, 0),
    # --- safety and orientation ---
    "safety_firstAidCabinet_wallMounted_a01": (4.9, 1.2, -2.6, 270),
    "safety_eyewashStation_stand_selfContained_a01": (-4.6, 0, 3.6, 90),
    "fireProtection_extinguisher_portable_a01": (4.7, 0, -3.8, 0),
    "screen_hospital_a01": (-3.6, 0, -1.2, 0),
    "stool_mobile_padded_vinyl_a01": (-1.9, 0, -2.0, 0),
    "sofa_visitor_a01": (0.2, 0, -3.6, 0),
    "lamp_uvc_a01": (1.8, 0, -1.9, 0),
    "ultrasound_a01": (1.0, 0, -0.1, 0),
    "sign_hospital_sidewall_a01": (0.0, 1.9, 3.95, 180),
    "digitalSystem_infoKiosk_vertical_a01": (-4.5, 0, -3.2, 90),
    "light_surgical_ceiling_arm_a01": (-3.0, 2.0, 0.9, 0),
}

SHOTS = [
    ("20_bed_bay", (-0.5, 1.7, -1.0), (-3.0, 0.6, -3.0)),
    ("21_front_wall", (1.8, 1.6, -1.2), (1.8, 0.9, -3.9)),
    ("22_left_wall", (-2.2, 1.7, 0.6), (-4.6, 0.9, 0.4)),
    ("23_table", (0.9, 1.7, 0.6), (0.9, 0.9, 2.6)),
    ("24_overview_front", (0.0, 2.9, -3.8), (0.0, 0.6, 1.0)),
    ("25_overview_back_left", (-4.6, 2.8, 3.7), (1.0, 0.6, -1.5)),
    ("26_overview_back_right", (4.6, 2.8, 3.7), (-1.0, 0.6, -1.5)),
    ("27_imaging_right", (1.6, 1.8, -0.2), (4.0, 0.9, 1.4)),
    ("28_surgical_left", (-1.0, 1.8, -0.2), (-3.2, 0.9, 1.0)),
    ("29_trolley_instruments", (-1.2, 1.8, -1.2), (-1.2, 0.9, -0.3)),
]

eq = {i['id'] for i in json.load(io.open(R + 'Data/Equipment/equipment.json', encoding='utf-8'))['items']}
items = []
missing_slots = [i for i in eq if i not in SLOTS]
if missing_slots:
    raise SystemExit('no layout slot for: ' + ', '.join(missing_slots))
order = [i for i in SLOTS if i in eq]
# hosts must come before the things resting on them
placed = set()
ordered = []
pending = list(order)
while pending:
    progressed = False
    for i in list(pending):
        s = SLOTS[i]
        if s[0] == 'on' and s[1] not in placed:
            if s[1] not in eq:      # host missing: drop the item rather than float it
                pending.remove(i); progressed = True
            continue
        ordered.append(i); placed.add(i); pending.remove(i); progressed = True
    if not progressed:
        raise SystemExit('layout dependency cycle: ' + ', '.join(pending))
for i in ordered:
    s = SLOTS[i]
    if s[0] == 'on':
        items.append({"id": i, "x": 0, "y": 0, "z": 0, "yaw": s[4], "restOn": s[1], "dx": s[2], "dz": s[3]})
    else:
        items.append({"id": i, "x": s[0], "y": s[1], "z": s[2], "yaw": s[3], "restOn": "", "dx": 0, "dz": 0})
layout = {
    "zoneTable": "table_metal_stainless_a03", "zoneObject": "box_corrugated_brown_b20", "zoneDx": 0.5, "zoneDz": 0.0,
    "items": items,
    "shots": [{"name": n, "pos": {"x": p[0], "y": p[1], "z": p[2]}, "look": {"x": l[0], "y": l[1], "z": l[2]}} for n, p, l in SHOTS],
}
os.makedirs(R + 'Data/Layout', exist_ok=True)
io.open(R + 'Data/Layout/layout.json', 'w', encoding='utf-8', newline='\n').write(json.dumps(layout, indent=2) + '\n')
print(len(items), 'layout items;', len(eq) - len(items), 'equipment entries dropped (host missing)')
