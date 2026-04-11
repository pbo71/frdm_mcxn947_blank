#ifndef AUDIO_CODEC_H_
#define AUDIO_CODEC_H_

#ifdef __cplusplus
extern "C" {
#endif

#include "fsl_common.h"

status_t AudioCodec_Init(void);
status_t AudioCodec_GetInitStatus(void);
uint32_t AudioCodec_GetInitAttemptCount(void);
uint32_t AudioCodec_GetLastInitStep(void);

#ifdef __cplusplus
}
#endif

#endif