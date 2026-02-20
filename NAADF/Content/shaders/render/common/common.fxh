#pragma once

#define FLATTEN_INDEX(pos, strideY, strideZ) ( mad((pos).z, (strideZ), mad((pos).y, (strideY), (pos).x)) )

#include "../../settings.fxh"
#include "commonConstants.fxh"
#include "commonRenderPipeline.fxh"
#include "commonRayTracing.fxh"
#include "commonColorCompression.fxh"
#include "commonOther.fxh"