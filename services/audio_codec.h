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
uint32_t AudioCodec_GetLastPlaybackEnableStep(void);
status_t AudioCodec_GetLastPlaybackEnableStatus(void);
status_t AudioCodec_EnablePlaybackDigitalPath(void);
status_t AudioCodec_RefreshPlaybackDigitalPath(void);
status_t AudioCodec_DisablePlaybackDigitalPath(void);
status_t AudioCodec_RecoverI2cBus(void);
status_t AudioCodec_GetLastRecoveryStatus(void);
uint32_t AudioCodec_GetLastRecoveryAttemptCount(void);

#ifdef __cplusplus
}
#endif

#endif