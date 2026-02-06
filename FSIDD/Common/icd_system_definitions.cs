using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{

    //constexpr uint16_t error_not_found = (uint16_t)60404;
    //constexpr uint8_t unit_or_module_not_found = 255; // this is an error

    public enum subsystem_ids : byte
    {
        rws_id = 1,
        sws_id = 2,
        mws_id = 3
    }

    public enum rws_submodule_ids : byte
    {
        rws_fla = 1,
        rws_sensors = 2,
        rws_left_manipulator = 4,
        rws_right_manipulator = 5,
        rws_vc_robot_sw = 6,
        rws_general = 7
    }

    public enum mws_submodule_ids : byte
    {
        mws_narrow_field_image = 2,
        mws_wide_field_image = 8,
        mws_vc_mic_sw = 9,
        mws_xyz_motion_axes = 21,

        mws_miccb_general = 0,
        mws_miccb_sensors = 1,

        mws_light_sources_coax_left = 0,
        mws_light_sources_coax_right = 1,
        mws_light_sources_parax = 2,
        mws_light_sources_tracking = 3,
        mws_mocb_sensors = 4,
        mws_mocb_general = 5
    }

    public enum sws_submodule_ids : byte
    {
        sws_ergonomics = 2,
        sws_sensors = 3,
        sws_left = 4,
        sws_right = 5,
        sws_vc_robot_sw = 6,
        sws_general = 7
    }

    public enum mws_units : byte
    {
        mws_axis_x = 1,
        mws_axis_y = 2,
        mws_axis_z = 3,
        mws_vc_ = 12,
        mws_vc_aug = 15,
        mws_vc_renderer = 16,
        mws_vc_mic = 17,

        mws_unit_miccb_general = 0,
        mws_unit_miccb_sensors = 1,

        mws_unit_coax_left = 0,
        mws_unit_coax_right = 1,
        mws_unit_parax = 2,
        mws_unit_tracking = 3,
        mws_unit_mocb_sensors = 4,
        mws_unit_mocb_general = 5,
        mws_unit_roll = 6,
        mws_unit_iris = 7
    }

    public enum rws_units : byte
    {
        rws_axis_m1 = 1,
        rws_axis_m2 = 2,
        rws_axis_m3 = 3,
        rws_axis_m4 = 4,
        rws_axis_m5 = 5,
        rws_axis_m6 = 6,
        rws_axis_m7 = 7,
        rws_4mb = 10,
        rws_eef = 11,
        rws_m5b = 12,
        rws_rcb = 13,
        rws_vc_rks = 15,
        rws_fla = 16,
        rws_sensors = 21,
        rws_left_manipulator = 22,
        rws_right_manipulator = 23
    }

    public enum sws_units : byte
    {
        sws_vcm_drive_1 = 1,
        sws_vcm_drive_2 = 2,
        sws_vcm_drive_3 = 3,
        sws_encoder_x = 11,
        sws_encoder_y = 12,
        sws_encoder_z = 13,
        sws_encoder_yaw = 14,
        sws_encoder_pitch = 15,
        sws_encoder_roll = 16,
        sws_handle = 17,
        sws_vc_rks = 18,
        sws_table_axis = 21,
        sws_3d_screen = 22,
        sws_other = 99
    }
}

  
