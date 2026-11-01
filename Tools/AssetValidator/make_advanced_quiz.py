"""Builds Data/Scenarios/identification_advanced.json (Module F) from the equipment catalogue and the room layout.
Each question asks the learner to find one item; hints come from the item's category and its real position in the layout,
and the explanation is the item's description. Run after make_layout.py."""
import io, json

R = 'D:/MedSimXR/'
CORE = {"chair_patient_mobile_a01", "CT_injection_system_a01", "box_corrugated_brown_b20", "rack_large_empty_a02", "monitor_patient_a01",
        "washBasin_a01", "bin_waste_a01", "cart_medical_utility_a01", "bed_hospital_standard_a01", "cabinet_for_drugs_a01"}
# preferred order: the learner-requested themes first (robots, X-ray, surgery tools), then the rest
PRIORITY = ["robot_surgical_c01", "Xray_machine_buckyTable_a01", "Xray_machine_buckyStand_a01", "anesthesia_machine_a01", "defibrillator_a01",
            "instrument_artery_forceps_b01", "instrument_bone_rongeurs_b01", "instrument_bone_mallet_b01", "instrument_hand_drill_b01",
            "stapler_surgical_a01", "needle_surgical_a01", "unit_electrosurgical_a01", "lung_ventilation_artificial_a01", "robotics_small_bot_a01",
            "Xray_machine_HVgenerator_a01", "Xray_machine_XrayArmRack_a01", "trolley_emergency_a01", "IV_pole_a01", "pump_infusion_a01",
            "wheelChair_c01", "table_surgical_a01", "light_surgical_ceiling_arm_a01", "sink_surgical_a01", "safety_firstAidCabinet_wallMounted_a01",
            "safety_eyewashStation_stand_selfContained_a01", "fireProtection_extinguisher_portable_a01", "trolley_instrument_b01", "trolley_cleaning_a01",
            "table_bedside_b01", "stethoscope_electronic_a01", "thermometer_fever_a01", "syringe_a01", "medicine_a03", "box_glove_a01",
            "tool_power_surgical_b01", "instrument_bone_forceps_b01", "hemodialysis_machine_a01", "device_endoscopy_system_b01", "laser_system_a01",
            "ultrasound_a01", "screen_hospital_a01", "chair_hospital_a01", "stool_mobile_padded_vinyl_a01", "disinfectant_a01", "lamp_uvc_a01",
            "warmer_infusion_a01", "tray_sterilization_instrument_c02", "appliance_handDryer_automatic_a01", "cleaning_soapDispenser_a01",
            "table_metal_stainless_a03", "device_vein_finder_a01", "sofa_visitor_a01", "unit_head_bed_a01", "light_head_bed_a01"]
MAX_Q = 20

eq = {i['id']: i for i in json.load(io.open(R + 'Data/Equipment/equipment.json', encoding='utf-8'))['items']}
lay = json.load(io.open(R + 'Data/Layout/layout.json', encoding='utf-8'))
pos = {}
host = {}
for it in lay['items']:
    pos[it['id']] = (it['x'], it['y'], it['z'])
    if it['restOn']:
        host[it['id']] = it['restOn']
for i, h in host.items():          # resting items take their host's position
    pos[i] = pos.get(h, pos[i])


def where(i):
    x, _, z = pos[i]
    side = 'left' if x < -1.7 else ('right' if x > 1.7 else 'middle')
    depth = 'front, near the entrance' if z < -1.3 else ('back' if z > 1.3 else 'middle')
    if side == 'middle':
        return "It is in the middle of the room, toward the %s." % depth
    return "It is on the %s side of the room, toward the %s." % (side, depth)


def first_sentence(t):
    return t.split('. ')[0].rstrip('.') + '.'


cands = [i for i in PRIORITY if i in eq and i not in CORE] + [i for i in eq if i not in CORE and i not in PRIORITY]
chosen = cands[:MAX_Q]
qs = []
for n, i in enumerate(chosen, 1):
    e = eq[i]
    hints = ["It belongs to the '%s' group of equipment." % e['category'].lower(), where(i)]
    if i in host and host[i] in eq:
        hints[1] = "It is on the %s. %s" % (eq[host[i]]['displayName'].lower(), where(i))
    qs.append({
        "id": "f%02d" % n, "objectId": i, "correctObjectId": i, "prompt": "Locate the %s." % e['displayName'],
        "hints": hints, "explanation": first_sentence(e['description']),
        "assessmentCriteria": "Identifies " + e['displayName'] + " in the room.", "maxAttempts": 3, "targetSeconds": 40})
out = {"moduleId": "F_advanced_identification", "title": "Module F - Advanced Equipment Identification",
       "reviewStatus": "PENDING_CLINICAL_REVIEW - generic descriptions, hints generated from the room layout", "questions": qs}
io.open(R + 'Data/Scenarios/identification_advanced.json', 'w', encoding='utf-8', newline='\n').write(json.dumps(out, indent=2, ensure_ascii=False) + '\n')
print(len(qs), 'advanced questions')
for q in qs[:3]:
    print(q['prompt'], '|', q['hints'], '|', q['explanation'])
