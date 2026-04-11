
# Copyright 2026 NXP
#
# SPDX-License-Identifier: BSD-3-Clause

cmake_minimum_required(VERSION 3.22.0)

cmake_path(APPEND conf_file_path ${CMAKE_CURRENT_LIST_DIR} ${board}_${core_id} prj.conf)
set(CONF_FILE ${conf_file_path} ${CONF_FILE})
include(${SdkRootDirPath}/cmake/extension/mcux.cmake)

project(audio_stream_demo_app LANGUAGES C CXX ASM PROJECT_BOARD_PORT_PATH ${board_root}/${board}/demo_apps/led_blinky)


include("${board}_${core_id}/board_files.cmake")
include(${SdkRootDirPath}/CMakeLists.txt)


mcux_add_source(
    SOURCES main.cpp
            alive_service.cpp
            alive_service.h
            audio_tx_service.cpp
            audio_tx_service.h
            freertos_diagnostics.cpp
            services/audio_codec.cpp
            services/audio_codec.h
            services/audio_playback_buffer.cpp
            services/audio_playback_buffer.h
            services/audio_stream_service.cpp
            services/audio_stream_service.h
            services/control_plane_service.cpp
            services/control_plane_service.h
            device_protocol/protocol_core.cpp
            device_protocol/protocol_core.hpp
            device_protocol/protocol_dispatch.cpp
            device_protocol/protocol_dispatch.hpp
            device_protocol/protocol_system_commands.cpp
            device_protocol/protocol_system_commands.hpp
            device_protocol/protocol_led_commands.cpp
            device_protocol/protocol_led_commands.hpp
            device_protocol/protocol.cpp
            device_protocol/protocol.hpp
            device_protocol/protocol_c_api.h
            FreeRTOSConfig.h
)

mcux_add_source(
    SOURCES usb_vendor_bulk.c
            usb_vendor_bulk.h
            usb_vendor_bulk_descriptor.c
            usb_vendor_bulk_descriptor.h
            usb_hw_init.c
)

mcux_add_include(
    INCLUDES audio_stream_demo_app
)
mcux_add_include(
    INCLUDES device_protocol
)
mcux_add_include(
    INCLUDES services
)

target_sources(${MCUX_SDK_PROJECT_NAME} PRIVATE
    ${SdkRootDirPath}/drivers/sai/fsl_sai.c
    ${SdkRootDirPath}/drivers/lpflexcomm/lpi2c/fsl_lpi2c.c
)

target_include_directories(${MCUX_SDK_PROJECT_NAME} PRIVATE
    ${SdkRootDirPath}/drivers/sai
    ${SdkRootDirPath}/drivers/lpflexcomm/lpi2c
    ${SdkRootDirPath}/components/codec/sgtl5000
    ${SdkRootDirPath}/components/codec/i2c
    ${SdkRootDirPath}/components/i2c
)

mcux_convert_binary(BINARY ${APPLICATION_BINARY_DIR}/${MCUX_SDK_PROJECT_NAME}.bin)

mcux_add_include(INCLUDES .)

mcux_add_linker_symbol(
    SYMBOLS "__stack_size__=0x2000\
             __heap_size__=0x1000"
)

